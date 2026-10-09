using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.DTOs;
using NibrasWeb.Enums;

namespace NibrasWeb.Service
{
    public class OfferService : IOfferService
    {
        private readonly NebrasdbContext _context;
        private readonly ILogger<OfferService> _logger;

        public OfferService(NebrasdbContext context, ILogger<OfferService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<OfferDto?> GetByIdAsync(int offerId)
        {
            var offer = await _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(o => o.Id == offerId);

            if (offer == null) return null;
            return await ToDtoAsync(offer);
        }

        public async Task<List<OfferDto>> GetOffersForLandAsync(int landId, int requestingUserId)
        {
            var user = await GetUserAsync(requestingUserId);

            var land = await _context.Lands.FirstOrDefaultAsync(l => l.Id == landId)
                ?? throw new KeyNotFoundException("Land not found.");

            var isAdmin = user.Role.Name == RoleNames.Admin || user.Role.Name == RoleNames.SuperAdmin;
            var isLandlord = land.LandlordId == requestingUserId;
            var isInvestor = user.Role.Name == RoleNames.Investor;

            if (!isAdmin && !isLandlord && !isInvestor)
                throw new UnauthorizedAccessException("You are not authorized to view offers for this land.");

            var query = _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .Where(o => o.LandId == landId);

            if (!isAdmin && !isLandlord)
                query = query.Where(o => o.InvestorId == requestingUserId);

            var offers = await query
                .OrderByDescending(o => o.CurrentAmount) // use the thenby
                .ToListAsync();

            var result = new List<OfferDto>();
            foreach (var offer in offers)
                result.Add(await ToDtoAsync(offer));

            return result;
        }

        public async Task<List<OfferDto>> GetOfferDtosAsync(int investorId, string? statusFilter = null) 
        {
            var user = await GetUserAsync(investorId);

            if (user.Role.Name != RoleNames.Investor &&
                user.Role.Name != RoleNames.Admin &&
                user.Role.Name != RoleNames.SuperAdmin)
            {
                throw new UnauthorizedAccessException("Only investors or admins can list offers."); // requestingAdminId add it 
            }

            var query = _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Landlord)
                .Include(o => o.Investor)
                .Include(o => o.OfferVersions).ThenInclude(v => v.CreatedBy).ThenInclude(u => u.Role)
                .AsQueryable();

            if (user.Role.Name == RoleNames.Investor)
                query = query.Where(o => o.InvestorId == investorId);// we have to filter the offer rot the investor 

            if (!string.IsNullOrWhiteSpace(statusFilter))
                query = query.Where(o => o.Status == ParseStatus(statusFilter));//ParseStatus have to cheek 

            var offers = await query.OrderByDescending(o => o.UpdatedAt).ToListAsync();

            var result = new List<OfferDto>();
            foreach (var offer in offers)
                result.Add(await ToDtoAsync(offer));

            return result;
        }

        public async Task<List<OfferDto>> GetPendingAdminApprovalAsync()
        {
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
            var user = await GetUserAsync(request.InvestorId);
            if (user.Role.Name != RoleNames.Investor)
                throw new UnauthorizedAccessException("Only investors can create offers.");

            var land = await _context.Lands
                .FirstOrDefaultAsync(l => l.Id == request.LandId)
                ?? throw new KeyNotFoundException("Land not found.");

            if (land.Status != LandStatus.Verified)
                throw new InvalidOperationException("Only verified lands can receive offers.");

            var hasBlockingOffer = await _context.Offers.AnyAsync(o =>
                o.LandId == request.LandId &&
                (o.Status == OfferStatus.PendingAdminApproval ||
                 o.Status == OfferStatus.Accepted ||
                 o.Status == OfferStatus.Closed));

            if (hasBlockingOffer)
                throw new InvalidOperationException("This land already has an active or closed offer and is not available.");

            var grid = await _context.Grids
                .Include(g => g.GridCapacityReservations)
                .FirstOrDefaultAsync(g => g.Id == land.GridId)
                ?? throw new KeyNotFoundException("No grid found for this land.");

            var totalReservedMw = grid.GridCapacityReservations.Sum(r => r.ReservedMw);
            if ((totalReservedMw + request.RequiredCapacityMw) > grid.CapacityMw)
                throw new InvalidOperationException("No capacity left in this electrical grid.");

            var offer = new Offer
            {
                LandId = request.LandId,
                InvestorId = request.InvestorId,
                Status = OfferStatus.PendingLandlordResponse,
                ConnectionMechanism = request.ConnectionMechanism,
                CurrentAmount = request.LandlordSharePct ?? 0,
                LastProposedById = request.InvestorId,
                RequiredCapacityMw = request.RequiredCapacityMw,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
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
                CreatedById = request.InvestorId,
                IsCurrent = true,
                CreatedAt = DateTime.UtcNow
            };

            offer.OfferVersions.Add(version);

            offer.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                ActionType = OfferHistoryActionType.Created,
                ActorId = request.InvestorId,
                Amount = offer.CurrentAmount,
                Message = "Offer created.",
                CreatedAt = DateTime.UtcNow
            });

            _context.Offers.Add(offer);
            await _context.SaveChangesAsync();

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Offer creation failed.");
        }

        public async Task<OfferDto> CounterOfferAsync(int offerId, int userId, decimal newAmount, string? message)
        {
            var offer = await GetOfferForActionAsync(offerId, userId);

            if (newAmount <= 0)
                throw new ArgumentException("Counter amount must be greater than zero.");

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new InvalidOperationException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new UnauthorizedAccessException("Only the landlord can respond at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new UnauthorizedAccessException("Only the investor can respond at this stage.");

            var newVersionNumber = (offer.OfferVersions.Max(v => (int?)v.VersionNumber) ?? 0) + 1;

            var previousCurrent = offer.OfferVersions.Where(v => v.IsCurrent).ToList();
            foreach (var v in previousCurrent) v.IsCurrent = false;

            var version = new OfferVersion
            {
                OfferId = offer.Id,
                VersionNumber = newVersionNumber,
                LandlordSharePct = newAmount,
                Message = message,
                CreatedById = userId,
                IsCurrent = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.OfferVersions.Add(version);

            offer.CurrentAmount = newAmount;
            offer.LastProposedById = userId;
            offer.Status = isLandlord ? OfferStatus.PendingInvestorResponse : OfferStatus.PendingLandlordResponse;
            offer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Counter offer failed.");
        }

        public async Task<OfferDto> AcceptAsync(int offerId, int userId)
        {
            var offer = await GetOfferForActionAsync(offerId, userId);

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new InvalidOperationException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new UnauthorizedAccessException("Only the landlord can accept at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new UnauthorizedAccessException("Only the investor can accept at this stage.");

            offer.Status = OfferStatus.PendingAdminApproval;
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

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Accept failed.");
        }

        public async Task<OfferDto> RejectAsync(int offerId, int userId, string reason)
        {
            var offer = await GetOfferForActionAsync(offerId, userId);

            if (offer.Status != OfferStatus.PendingLandlordResponse &&
                offer.Status != OfferStatus.PendingInvestorResponse)
            {
                throw new InvalidOperationException("This offer is not in a negotiable state.");
            }

            var actor = await GetUserAsync(userId);
            var isLandlord = actor.Role.Name == RoleNames.Landlord;

            if (offer.Status == OfferStatus.PendingLandlordResponse && !isLandlord)
                throw new UnauthorizedAccessException("Only the landlord can reject at this stage.");
            if (offer.Status == OfferStatus.PendingInvestorResponse && isLandlord)
                throw new UnauthorizedAccessException("Only the investor can reject at this stage.");

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

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Reject failed.");
        }

        public async Task<OfferDto> WithdrawAsync(int offerId, int investorId)
        {
            var offer = await GetOfferForActionAsync(offerId, investorId);

            var actor = await GetUserAsync(investorId);
            if (actor.Role.Name != RoleNames.Investor)
                throw new UnauthorizedAccessException("Only investors can withdraw offers.");
            if (offer.InvestorId != investorId)
                throw new UnauthorizedAccessException("You can only withdraw your own offer.");

            if (offer.Status == OfferStatus.Accepted ||
                offer.Status == OfferStatus.Closed ||
                offer.Status == OfferStatus.RejectedByAdmin)
            {
                throw new InvalidOperationException("This offer cannot be withdrawn in its current state.");
            }

            offer.Status = OfferStatus.WithdrawnByInvestor;
            offer.UpdatedAt = DateTime.UtcNow;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.Withdrawn,
                ActorId = investorId,
                Amount = offer.CurrentAmount,
                Message = "Offer withdrawn by investor.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Withdraw failed.");
        }

        public async Task<OfferDto> AdminApproveAsync(int offerId, int adminId) // we need transaction for the security
        {
            var admin = await GetUserAsync(adminId);
            if (admin.Role.Name != RoleNames.Admin && admin.Role.Name != RoleNames.SuperAdmin)
                throw new UnauthorizedAccessException("Only admins can approve offers.");

            var offer = await _context.Offers
                .Include(o => o.Land).ThenInclude(l => l.Grid).ThenInclude(g => g.GridCapacityReservations)
                .Include(o => o.OfferVersions)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new KeyNotFoundException("Offer not found.");

            if (offer.Status != OfferStatus.PendingAdminApproval)
                throw new InvalidOperationException("Only offers pending admin approval can be approved.");

            var currentVersion = offer.OfferVersions.FirstOrDefault(v => v.IsCurrent)
                ?? offer.OfferVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (currentVersion == null)
                throw new InvalidOperationException("Offer has no versions to approve.");

            var totalReservedMw = offer.Land.Grid.GridCapacityReservations.Sum(r => r.ReservedMw);
            if ((totalReservedMw + offer.RequiredCapacityMw) > offer.Land.Grid.CapacityMw)
                throw new InvalidOperationException("No capacity left in this electrical grid.");

            _context.GridCapacityReservations.Add(new GridCapacityReservation
            {
                GridId = offer.Land.GridId,
                OfferId = offer.Id,
                ReservedMw = offer.RequiredCapacityMw,
                ReservationType = "Offer",
                CreatedAt = DateTime.UtcNow
            });

            offer.AcceptedVersionId = currentVersion.Id;
            offer.Status = OfferStatus.Accepted;
            offer.UpdatedAt = DateTime.UtcNow;

            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.ApprovedByAdmin,
                ActorId = adminId,
                Amount = offer.CurrentAmount,
                Message = "Offer approved by admin.",
                CreatedAt = DateTime.UtcNow
            });

            var competitors = await _context.Offers
                .Where(o => o.LandId == offer.LandId &&
                            o.Id != offer.Id &&
                            (o.Status == OfferStatus.PendingAdminApproval ||
                             o.Status == OfferStatus.PendingLandlordResponse ||
                             o.Status == OfferStatus.PendingInvestorResponse))
                .ToListAsync();

            foreach (var competitor in competitors)
            {
                competitor.Status = OfferStatus.Closed;
                competitor.UpdatedAt = DateTime.UtcNow;

                _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
                {
                    OfferId = competitor.Id,
                    ActionType = OfferHistoryActionType.CompetitorClosed,
                    ActorId = adminId,
                    Amount = competitor.CurrentAmount,
                    Message = "Closed because a competing offer on the same land was approved.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Approval failed.");
        }

        public async Task<OfferDto> AdminRejectAsync(int offerId, int adminId, string reason) // it will be stored in Version + History
        {
            var admin = await GetUserAsync(adminId);
            if (admin.Role.Name != RoleNames.Admin && admin.Role.Name != RoleNames.SuperAdmin)
                throw new UnauthorizedAccessException("Only admins can reject offers.");

            var offer = await GetOfferForActionAsync(offerId, adminId);

            if (offer.Status != OfferStatus.PendingAdminApproval)
                throw new InvalidOperationException("Only offers pending admin approval can be rejected.");

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

            return await GetByIdAsync(offer.Id) ?? throw new InvalidOperationException("Rejection failed.");
        }

        public async Task<List<OfferNegotiationHistoryDto>> GetNegotiationHistoryAsync(int offerId, int requestingUserId) // foreach + IsCurrent
        {
            var user = await GetUserAsync(requestingUserId);
            var offer = await _context.Offers
                .Include(o => o.Land)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new KeyNotFoundException("Offer not found.");

            var isAdmin = user.Role.Name == RoleNames.Admin || user.Role.Name == RoleNames.SuperAdmin;
            var isParty = offer.InvestorId == requestingUserId || offer.Land.LandlordId == requestingUserId;
            if (!isAdmin && !isParty)
                throw new UnauthorizedAccessException("You are not authorized to view this negotiation history.");

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

        public async Task MarkOfferClosedAsync(int offerId, int contractId, int performedByUserId)
        {
            var offer = await _context.Offers.FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new KeyNotFoundException("Offer not found.");

            var contractExists = await _context.Contracts.AnyAsync(c => c.Id == contractId);
            if (!contractExists)
                throw new KeyNotFoundException("Contract not found.");

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

        private async Task<User> GetUserAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId)
                ?? throw new KeyNotFoundException("User not found.");
        }

        private async Task<Offer> GetOfferForActionAsync(int offerId, int userId)
        {
            var offer = await _context.Offers
                .Include(o => o.Land)
                .Include(o => o.OfferVersions)
                .FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new KeyNotFoundException("Offer not found.");

            var user = await GetUserAsync(userId);
            var isAdmin = user.Role.Name == RoleNames.Admin || user.Role.Name == RoleNames.SuperAdmin;
            var isParty = offer.InvestorId == userId || offer.Land.LandlordId == userId;
            if (!isAdmin && !isParty)
                throw new UnauthorizedAccessException("You are not authorized to act on this offer.");

            return offer;
        }

        private static OfferStatus ParseStatus(string status)
        {
            if (Enum.TryParse<OfferStatus>(status, true, out var parsed))
                return parsed;
            throw new ArgumentException($"Unknown offer status: {status}");
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
                CreatedByName = version.CreatedBy?.FullName,
                CreatedByRole = version.CreatedBy?.Role?.Name
            };
        }
    }
}
