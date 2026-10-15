using System.Data;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.DTOs;
using NibrasWeb.Enums;

namespace NibrasWeb.Service
{
    public class OfferService : IOfferService
    {
        private readonly NebrasdbContext _context;
        private readonly ILogger<OfferService> _logger;
        private readonly ICurrentUser _currentUser;
        private readonly IConfiguration _configuration;

        public OfferService(
            NebrasdbContext context,
            ILogger<OfferService> logger,
            ICurrentUser currentUser,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _currentUser = currentUser;
            _configuration = configuration;
        }

        public async Task<OfferDto> GetByIdAsync(int offerId)
        {
            var offer = await LoadOfferAsync(offerId);
            EnsureCanRead(offer);
            return await ToDtoAsync(offer);
        }

        public async Task<List<OfferDto>> GetOffersForLandAsync(int landId)
        {
            var userId = RequireUserId();

            var land = await _context.Lands
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == landId)
                ?? throw new NotFoundException("Land not found.");

            if (!IsAdmin && land.LandlordId != userId)
                throw new ForbiddenException("Only the land owner or an admin can view offers for this land.");

            var offers = await _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .Where(o => o.LandId == landId)
                .OrderByDescending(o => o.CurrentAmount)
                .ToListAsync();

            var result = new List<OfferDto>();
            foreach (var offer in offers)
                result.Add(await ToDtoAsync(offer));

            return result;
        }

        public async Task<List<OfferDto>> GetMyOffersAsync(string? statusFilter = null)
        {
            var userId = RequireUserId();

            if (_currentUser.Role != RoleNames.Investor && !IsAdmin)
                throw new ForbiddenException("Only investors or admins can list offers.");

            var query = _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .AsQueryable();

            if (_currentUser.Role == RoleNames.Investor)
                query = query.Where(o => o.InvestorId == userId);

            if (!string.IsNullOrWhiteSpace(statusFilter))
                query = query.Where(o => o.Status == ParseStatus(statusFilter));

            var offers = await query.OrderByDescending(o => o.UpdatedAt).ToListAsync();

            var result = new List<OfferDto>();
            foreach (var offer in offers)
                result.Add(await ToDtoAsync(offer));

            return result;
        }

        public async Task<List<OfferDto>> GetPendingAdminApprovalAsync()
        {
            EnsureAdmin("Only admins can view offers pending admin approval.");

            var offers = await _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .Where(o => o.Status == OfferStatus.PendingAdminApproval)
                .OrderByDescending(o => o.UpdatedAt)
                .ToListAsync();

            var result = new List<OfferDto>();
            foreach (var offer in offers)
                result.Add(await ToDtoAsync(offer));

            return result;
        }

        public async Task<OfferDto> CreateAsync(CreateOfferRequest request)
        {
            // BR-O1 / BR-S1: identity comes from the token, never from the request body.
            var userId = RequireUserId();

            var land = await _context.Lands.FirstOrDefaultAsync(l => l.Id == request.LandId)
                ?? throw new NotFoundException("Land not found.");

            // BR-O1: an investor cannot make an offer on their own land. Chosen as 409
            // (illegal state), consistent with the other create-time business rules. This
            // check runs before the investor-role gate so an owner-landlord is blocked with
            // 409 while any other non-investor is blocked with 403 by the role gate below.
            if (land.LandlordId == userId)
                throw new BusinessRuleException("An investor cannot make an offer on their own land.");

            var user = await GetUserAsync(userId);
            if (user.Role.Name != RoleNames.Investor)
                throw new ForbiddenException("Only investors can create offers.");
            if (!user.IsApproved)
                throw new ForbiddenException("Only approved investors can create offers.");

            if (!Enum.IsDefined(typeof(ConnectionMechanism), request.ConnectionMechanism))
                throw new ValidationFailedException("ConnectionMechanism is not a defined value.");

            // BR-O1: the land must be Verified (land status, not the offer statuses).
            if (land.Status != LandStatus.Verified)
                throw new BusinessRuleException("Only verified lands can receive offers.");

            // BR-O2 / D-14: one active (non-terminal) offer per investor per land.
            // Terminal = RejectedBy*, WithdrawnByInvestor, Closed. There is no DB index for
            // this in the existing schema, so it is enforced here in application code.
            // NOTE: no grid-capacity check at creation (BR-O1 has none; capacity is enforced
            // at admin approval per BR-O6). This also allows the S4 scenario where two
            // 6 MW offers are created on a 10 MW grid and only the second approval fails.
            var hasActiveOffer = await _context.Offers.AnyAsync(o =>
                o.LandId == land.Id &&
                o.InvestorId == userId &&
                (o.Status == OfferStatus.PendingLandlordResponse ||
                 o.Status == OfferStatus.PendingInvestorResponse ||
                 o.Status == OfferStatus.PendingAdminApproval ||
                 o.Status == OfferStatus.Accepted));

            if (hasActiveOffer)
                throw new BusinessRuleException("You already have an active offer on this land.");

            var now = DateTime.UtcNow;
            var offer = new Offer
            {
                LandId = land.Id,
                InvestorId = userId,
                Status = OfferStatus.PendingLandlordResponse,
                ConnectionMechanism = request.ConnectionMechanism,
                CurrentAmount = request.LandlordSharePct ?? 0,
                LastProposedById = userId,
                RequiredCapacityMw = request.RequiredCapacityMw,
                CreatedAt = now,
                UpdatedAt = now
            };

            var version = new OfferVersion
            {
                VersionNumber = 1,
                LandlordSharePct = request.LandlordSharePct,
                DurationYears = request.DurationYears,
                StartDate = request.StartDate,
                InstallationCost = request.InstallationCost,
                SolarCellCapacityKw = request.SolarCellCapacityKw,
                Message = request.Message,
                CreatedById = userId,
                IsCurrent = true,
                CreatedAt = now
            };

            offer.OfferVersions.Add(version);

            offer.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                ActionType = OfferHistoryActionType.Created,
                ActorId = userId,
                Amount = offer.CurrentAmount,
                Message = "Offer created.",
                CreatedAt = now
            });

            _context.Offers.Add(offer);
            await _context.SaveChangesAsync();

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Offer creation failed.");
        }

        public async Task<OfferDto> CounterOfferAsync(int offerId, CounterOfferRequest request)
        {
            var userId = RequireUserId();
            if (request.NewAmount <= 0)
                throw new ValidationFailedException("Counter amount must be greater than zero.");

            var offer = await GetOfferForActionAsync(offerId, userId);
            EnsureNotFrozenCompetitor(offer.Land.Status, offer.Status);

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new BusinessRuleException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new ForbiddenException("Only the landlord can respond at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new ForbiddenException("Only the investor can respond at this stage.");

            var now = DateTime.UtcNow;

            // Step 5.5 (BR-O4): because of the filtered unique index
            // UX_OFFER_VERSIONS_CURRENT_OFFER, persist the previous version's IsCurrent=false
            // with its own SaveChanges BEFORE inserting the new version, keeping both inside
            // one database transaction. A racing counter-offer violates the filtered unique
            // index and is reported as 409.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var previousCurrent = offer.OfferVersions.Where(v => v.IsCurrent).ToList();
                foreach (var v in previousCurrent) v.IsCurrent = false;
                await _context.SaveChangesAsync();

                var newVersionNumber = (offer.OfferVersions.Max(v => (int?)v.VersionNumber) ?? 0) + 1;

                var version = new OfferVersion
                {
                    OfferId = offer.Id,
                    VersionNumber = newVersionNumber,
                    LandlordSharePct = request.NewAmount,
                    Message = request.Message,
                    CreatedById = userId,
                    IsCurrent = true,
                    CreatedAt = now
                };

                _context.OfferVersions.Add(version);

                offer.CurrentAmount = request.NewAmount;
                offer.LastProposedById = userId;
                offer.Status = isLandlord ? OfferStatus.PendingInvestorResponse : OfferStatus.PendingLandlordResponse;
                offer.UpdatedAt = now;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UX_OFFER_VERSIONS_CURRENT_OFFER"))
            {
                throw new BusinessRuleException("A concurrent counter-offer was detected; please retry.");
            }

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Counter offer failed.");
        }

        public async Task<OfferDto> AcceptAsync(int offerId)
        {
            var userId = RequireUserId();
            var offer = await GetOfferForActionAsync(offerId, userId);
            EnsureNotFrozenCompetitor(offer.Land.Status, offer.Status);

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new BusinessRuleException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new ForbiddenException("Only the landlord can accept at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new ForbiddenException("Only the investor can accept at this stage.");

            // BR-O5: the accepted version is the current version.
            var currentVersion = offer.OfferVersions.FirstOrDefault(v => v.IsCurrent)
                ?? offer.OfferVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

            offer.Status = OfferStatus.PendingAdminApproval;
            offer.AcceptedVersionId = currentVersion?.Id;
            offer.UpdatedAt = DateTime.UtcNow;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.AcceptedPendingAdminReview,
                ActorId = userId,
                Amount = offer.CurrentAmount,
                Message = "Offer accepted, pending admin approval.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Accept failed.");
        }

        public async Task<OfferDto> RejectAsync(int offerId, string reason)
        {
            var userId = RequireUserId();

            if (string.IsNullOrWhiteSpace(reason))
                throw new ValidationFailedException("A rejection reason is required.");

            var offer = await GetOfferForActionAsync(offerId, userId);
            EnsureNotFrozenCompetitor(offer.Land.Status, offer.Status);

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new BusinessRuleException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new ForbiddenException("Only the landlord can reject at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new ForbiddenException("Only the investor can reject at this stage.");

            offer.Status = isLandlord ? OfferStatus.RejectedByLandlord : OfferStatus.RejectedByInvestor;
            offer.UpdatedAt = DateTime.UtcNow;

            var currentVersion = offer.OfferVersions.FirstOrDefault(v => v.IsCurrent)
                ?? offer.OfferVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (currentVersion != null)
                currentVersion.RejectionReason = reason;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = isLandlord ? OfferHistoryActionType.RejectedByLandlord : OfferHistoryActionType.RejectedByInvestor,
                ActorId = userId,
                Amount = offer.CurrentAmount,
                Message = reason,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Reject failed.");
        }

        public async Task<OfferDto> WithdrawAsync(int offerId)
        {
            var userId = RequireUserId();
            var offer = await GetOfferForActionAsync(offerId, userId);
            EnsureNotFrozenCompetitor(offer.Land.Status, offer.Status);

            var actor = await GetUserAsync(userId);
            if (actor.Role.Name != RoleNames.Investor)
                throw new ForbiddenException("Only investors can withdraw offers.");
            if (offer.InvestorId != userId)
                throw new ForbiddenException("You can only withdraw your own offer.");

            // BR-O3 / docs-01 §6: the investor may withdraw only BEFORE the offer reaches
            // PendingAdminApproval (i.e. only while it is still being negotiated). Terminal
            // statuses have no outgoing transitions.
            if (offer.Status is not (OfferStatus.PendingLandlordResponse or OfferStatus.PendingInvestorResponse))
                throw new BusinessRuleException("This offer cannot be withdrawn in its current state.");

            offer.Status = OfferStatus.WithdrawnByInvestor;
            offer.UpdatedAt = DateTime.UtcNow;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.Withdrawn,
                ActorId = userId,
                Amount = offer.CurrentAmount,
                Message = "Offer withdrawn by investor.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Withdraw failed.");
        }

        public async Task<OfferDto> AdminApproveAsync(int offerId)
        {
            var adminId = RequireUserId();
            EnsureAdmin("Only admins can approve offers.");

            // BR-O6 / D-08: ALL state changes happen in ONE serializable database
            // transaction. The offer, land and grid are re-read inside the transaction and
            // the grid row is pinned with an app-level UPDLOCK,HOLDLOCK (the same convention
            // as the Step-4 locking fix) so concurrent approvals on the same grid serialize;
            // the loser re-computes capacity after the winner commits and fails with 409
            // instead of oversubscribing the grid.
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var now = DateTime.UtcNow;

                var offer = await _context.Offers
                    .Include(o => o.OfferVersions)
                    .FirstOrDefaultAsync(o => o.Id == offerId)
                    ?? throw new NotFoundException("Offer not found.");
                if (offer.Status != OfferStatus.PendingAdminApproval)
                    throw new BusinessRuleException("Only offers pending admin approval can be approved.");

                var currentVersion = offer.OfferVersions.FirstOrDefault(v => v.IsCurrent)
                    ?? offer.OfferVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                if (currentVersion == null)
                    throw new BusinessRuleException("Offer has no versions to approve.");

                var land = await _context.Lands.FirstOrDefaultAsync(l => l.Id == offer.LandId)
                    ?? throw new NotFoundException("Land not found.");
                // Step 5 interim freeze: if another offer on this land is under active
                // negotiation, a competing offer cannot be approved. The selected offer is the
                // only Accepted offer on the land, so this never blocks its own workflow.
                EnsureNotFrozenCompetitor(land.Status, offer.Status);
                if (land.Status != LandStatus.Verified)
                    throw new BusinessRuleException("Only offers on Verified land can be approved.");

                var grid = await _context.Grids
                    .FromSqlInterpolated($"SELECT * FROM GRIDS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {land.GridId}")
                    .FirstOrDefaultAsync()
                    ?? throw new NotFoundException("Grid not found.");

                // Active reservations only: ReleasedAt IS NULL and (ExpiresAt IS NULL or in the future).
                var reservedMw = await _context.GridCapacityReservations
                    .Where(r => r.GridId == grid.Id &&
                                r.ReleasedAt == null &&
                                (r.ExpiresAt == null || r.ExpiresAt > now))
                    .SumAsync(r => (decimal?)r.ReservedMw) ?? 0m;

                if (reservedMw + offer.RequiredCapacityMw > grid.CapacityMw)
                    throw new BusinessRuleException("No capacity left in this electrical grid.");

                var holdDays = _configuration.GetValue<int?>("Grid:HoldDays");
                var expiry = now.AddDays(holdDays is > 0 ? holdDays.Value : 90);

                _context.GridCapacityReservations.Add(new GridCapacityReservation
                {
                    GridId = grid.Id,
                    OfferId = offer.Id,
                    ReservationType = "Offer",
                    ReservedMw = offer.RequiredCapacityMw,
                    CreatedAt = now,
                    ExpiresAt = expiry
                });

                offer.AcceptedVersionId = currentVersion.Id;
                offer.Status = OfferStatus.Accepted;
                offer.UpdatedAt = now;

                _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
                {
                    OfferId = offer.Id,
                    ActionType = OfferHistoryActionType.ApprovedByAdmin,
                    ActorId = adminId,
                    Amount = offer.CurrentAmount,
                    Message = "Offer approved by admin.",
                    CreatedAt = now
                });

                land.Status = LandStatus.UnderContract;
                _context.LandStatusHistories.Add(new LandStatusHistory
                {
                    LandId = land.Id,
                    Status = LandStatus.UnderContract,
                    ChangedById = adminId,
                    Reason = "Land placed under contract after an offer was approved.",
                    ChangedAt = now
                });

                // Approved rule (Step 5 correction): approving an offer does NOT close the other
                // offers on the land. Competitors keep their status and history and are frozen
                // by EnsureNotFrozenCompetitor. They are closed only when the contract reaches
                // ContractStatus.Active (Step 6 integration, not implemented in Step 5).
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ__GRID_CAP__OFFER_ID"))
            {
                throw new BusinessRuleException("This offer has already been approved.");
            }

            return await GetByIdInternalAsync(offerId) ?? throw new BusinessRuleException("Approval failed.");
        }

        public async Task<OfferDto> AdminRejectAsync(int offerId, string reason)
        {
            var adminId = RequireUserId();
            EnsureAdmin("Only admins can reject offers.");

            if (string.IsNullOrWhiteSpace(reason))
                throw new ValidationFailedException("A rejection reason is required.");

            var offer = await GetOfferForActionAsync(offerId, adminId);
            EnsureNotFrozenCompetitor(offer.Land.Status, offer.Status);

            if (offer.Status != OfferStatus.PendingAdminApproval)
                throw new BusinessRuleException("Only offers pending admin approval can be rejected.");

            offer.Status = OfferStatus.RejectedByAdmin;
            offer.UpdatedAt = DateTime.UtcNow;

            var currentVersion = offer.OfferVersions.FirstOrDefault(v => v.IsCurrent)
                ?? offer.OfferVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (currentVersion != null)
                currentVersion.RejectionReason = reason;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.RejectedByAdmin,
                ActorId = adminId,
                Amount = offer.CurrentAmount,
                Message = reason,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return await GetByIdInternalAsync(offer.Id) ?? throw new BusinessRuleException("Rejection failed.");
        }

        public async Task<List<OfferNegotiationHistoryDto>> GetNegotiationHistoryAsync(int offerId)
        {
            var userId = RequireUserId();

            var offer = await _context.Offers
                .Include(o => o.Land)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new NotFoundException("Offer not found.");

            if (!IsAdmin && offer.InvestorId != userId && offer.Land.LandlordId != userId)
                throw new ForbiddenException("You are not authorized to view this negotiation history.");

            var history = await _context.OfferNegotiationHistories
                .Include(h => h.Actor).ThenInclude(a => a.Role)
                .Where(h => h.OfferId == offerId)
                .OrderBy(h => h.CreatedAt)
                .ToListAsync();

            return history.Select(h => new OfferNegotiationHistoryDto
            {
                Id = h.Id,
                OfferId = h.OfferId,
                ActionType = h.ActionType,
                ActorId = h.ActorId,
                ActorName = h.Actor.FullName,
                ActorRole = h.Actor.Role.Name,
                Amount = h.Amount,
                Message = h.Message,
                CreatedAt = h.CreatedAt
            }).ToList();
        }

        // D-10: no public endpoint exposes this. It is internal (used by the contract
        // lifecycle) and enforces BOTH that the contract belongs to the specified offer and
        // that the offer is Accepted, as required by Step 5.2/5.4.
        public async Task MarkOfferClosedAsync(int offerId, int contractId, int performedByUserId)
        {
            var offer = await _context.Offers.FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new NotFoundException("Offer not found.");

            if (offer.Status != OfferStatus.Accepted)
                throw new BusinessRuleException("Only an accepted offer can be closed by contract creation.");

            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId)
                ?? throw new NotFoundException("Contract not found.");

            if (contract.OfferId != offerId)
                throw new BusinessRuleException("The contract does not belong to this offer.");

            offer.Status = OfferStatus.Closed;
            offer.UpdatedAt = DateTime.UtcNow;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.ClosedByContract,
                ActorId = performedByUserId,
                Amount = offer.CurrentAmount,
                Message = $"Offer closed after contract {contractId} was created.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        private bool IsAdmin => _currentUser.Role is RoleNames.Admin or RoleNames.SuperAdmin;

        private void EnsureAdmin(string message)
        {
            if (!IsAdmin)
                throw new ForbiddenException(message);
        }

        private int RequireUserId() =>
            _currentUser.Id ?? throw new ForbiddenException("Authentication is required.");

        // Step 5 interim freeze (approved rule): once an offer on a land reaches Accepted, that
        // land is UnderContract and is under active negotiation. Competing offers on the same
        // land keep their status and history but are frozen: their state-changing operations
        // are rejected with 409. The selected offer is the only Accepted offer on the land (a
        // second approval is impossible while the land is UnderContract), so it is never blocked
        // here. Closing competitors happens only when the contract becomes Active (Step 6).
        private static void EnsureNotFrozenCompetitor(LandStatus landStatus, OfferStatus offerStatus)
        {
            if (landStatus != LandStatus.UnderContract)
                return;
            if (offerStatus == OfferStatus.Accepted)
                return;
            throw new BusinessRuleException(
                "Another offer on this land is under active negotiation; competing offers cannot be changed until that negotiation concludes.");
        }

        private async Task<User> GetUserAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
                ?? throw new NotFoundException("User not found.");
        }

        private async Task<Offer> LoadOfferAsync(int offerId)
        {
            return await _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new NotFoundException("Offer not found.");
        }

        private async Task<OfferDto?> GetByIdInternalAsync(int offerId)
        {
            var offer = await LoadOfferAsync(offerId);
            return await ToDtoAsync(offer);
        }

        // BR-O9 / 404-vs-403 convention (SEC-09): a missing offer is 404; an existing offer
        // that the caller may not see is 403.
        private void EnsureCanRead(Offer offer)
        {
            var userId = RequireUserId();
            if (IsAdmin || offer.InvestorId == userId || offer.Land.LandlordId == userId)
                return;
            throw new ForbiddenException("You are not authorized to view this offer.");
        }

        private async Task<Offer> GetOfferForActionAsync(int offerId, int userId)
        {
            var offer = await _context.Offers
                .Include(o => o.Land)
                .Include(o => o.OfferVersions)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new NotFoundException("Offer not found.");

            var user = await GetUserAsync(userId);
            var isAdmin = user.Role.Name == RoleNames.Admin || user.Role.Name == RoleNames.SuperAdmin;
            var isParty = offer.InvestorId == userId || offer.Land.LandlordId == userId;
            if (!isAdmin && !isParty)
                throw new ForbiddenException("You are not authorized to act on this offer.");

            return offer;
        }

        private static bool IsUniqueViolation(DbUpdateException ex, string indexName)
        {
            if (ex.InnerException is not Microsoft.Data.SqlClient.SqlException sql)
                return false;
            return sql.Number is 2601 or 2627 &&
                   sql.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);
        }

        private static OfferStatus ParseStatus(string status)
        {
            if (Enum.TryParse<OfferStatus>(status, true, out var parsed))
                return parsed;
            throw new ValidationFailedException($"Unknown offer status: {status}");
        }

        private async Task<OfferDto> ToDtoAsync(Offer offer)
        {
            var latestVersion = offer.OfferVersions?
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();

            return new OfferDto
            {
                Id = offer.Id,
                LandId = offer.LandId,
                InvestorId = offer.InvestorId,
                Status = offer.Status,
                ConnectionMechanism = offer.ConnectionMechanism,
                CurrentAmount = offer.CurrentAmount,
                LastProposedById = offer.LastProposedById,
                RequiredCapacityMw = offer.RequiredCapacityMw,
                CreatedAt = offer.CreatedAt,
                UpdatedAt = offer.UpdatedAt,
                RelatedToRejectedOfferId = offer.RelatedToRejectedOfferId,
                InvestorName = offer.Investor?.FullName,
                LandLandlordName = offer.Land?.Landlord?.FullName,
                LatestVersion = latestVersion == null ? null : await ToVersionDtoAsync(latestVersion, offer)
            };
        }

        private async Task<OfferVersionDto> ToVersionDtoAsync(OfferVersion version, Offer offer)
        {
            var maxVersion = offer.OfferVersions?.Max(v => v.VersionNumber) ?? version.VersionNumber;

            return new OfferVersionDto
            {
                Id = version.Id,
                OfferId = version.OfferId,
                VersionNumber = version.VersionNumber,
                LandlordSharePct = version.LandlordSharePct,
                DurationYears = version.DurationYears,
                StartDate = version.StartDate,
                InstallationCost = version.InstallationCost,
                SolarCellCapacityKw = version.SolarCellCapacityKw,
                ExpectedAnnualRevenue = version.ExpectedAnnualRevenue,
                EffectiveCostPerKw = version.EffectiveCostPerKw,
                PaybackPeriodMonths = version.PaybackPeriodMonths,
                Message = version.Message,
                IsCurrent = version.IsCurrent || version.VersionNumber == maxVersion,
                CreatedAt = version.CreatedAt ?? DateTime.UtcNow,
                CreatedByName = version.CreatedBy?.FullName ?? string.Empty,
                CreatedByRole = version.CreatedBy?.Role?.Name ?? string.Empty
            };
        }
    }
}