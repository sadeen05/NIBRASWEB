using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;
using NibrasWeb.Service;

namespace NIBRAS.API.Services;

/// <summary>
/// Step 6 (Contracts MVP). Contract lifecycle: create from an Accepted offer (Admin),
/// both parties sign, Admin reviews (approve -> Active / reject -> Rejected), and Admin
/// terminates an Active contract. Review/terminate release the linked grid reservation and
/// return the land to Verified. Cancellation, force-terminate, background jobs and
/// ProjectApproval tracking are intentionally out of scope.
/// Step 7 hardening (F1/F2): creation refuses an expired hold, approval clears the
/// reservation's hold expiry, and rejection is permitted before both parties sign.
/// </summary>
public class ContractService : IContractService
{
    private const int MaxPageSize = 50;
    private const int ReleaseReasonMaxLength = 300; // GridCapacityReservation.ReleaseReason (max 300)
    private const string OfferContractConflictMessage = "A contract already exists for this offer.";

    private readonly NebrasdbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IOfferService _offerService;

    public ContractService(NebrasdbContext context, ICurrentUser currentUser, IOfferService offerService)
    {
        _context = context;
        _currentUser = currentUser;
        _offerService = offerService;
    }

    public async Task<ContractListResponse> GetAllAsync(int pageNumber, int pageSize, string? status)
    {
        if (pageNumber < 1)
            throw new ValidationFailedException("Page number must be at least 1.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationFailedException("Page size must be between 1 and 50.");

        var statusFilter = ParseStatus(status);

        var query = _context.Contracts
            .AsNoTracking()
            .Include(c => c.Land)
            .Include(c => c.Investor)
            .Include(c => c.Landlord)
            .Include(c => c.OfferVersion)
            .AsQueryable();

        if (!IsAdmin)
        {
            var userId = RequireUserId();
            query = _currentUser.Role == RoleNames.Landlord
                ? query.Where(c => c.LandlordId == userId)
                : _currentUser.Role == RoleNames.Investor
                    ? query.Where(c => c.InvestorId == userId)
                    : query.Where(c => false);
        }

        if (statusFilter is { } filter)
            query = query.Where(c => c.Status == filter);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // SEC-14 style guard: an offset beyond Int32 cannot exist in any result set, so it
        // yields an empty page instead of a negative OFFSET that SQL Server rejects.
        var offset = (long)(pageNumber - 1) * pageSize;
        if (offset > int.MaxValue)
            return new ContractListResponse([], pageNumber, pageSize, totalCount, totalPages);

        var contracts = await query
            .OrderBy(c => c.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync();

        return new ContractListResponse(contracts.Select(ToDto).ToList(), pageNumber, pageSize, totalCount, totalPages);
    }

    public async Task<ContractDto> GetByIdAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);
        return ToDto(contract);
    }

    public async Task<ContractDto> CreateAsync(CreateContractRequest request)
    {
        EnsureAdmin("Only admins can create contracts.");
        var adminId = RequireUserId();

        var offer = await _context.Offers
            .Include(o => o.Land)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId)
            ?? throw new NotFoundException("Offer not found.");

        if (offer.Status != OfferStatus.Accepted)
            throw new BusinessRuleException("Only an accepted offer can become a contract.");

        var hasLiveContract = await _context.Contracts.AnyAsync(c =>
            c.OfferId == offer.Id &&
            (c.Status == ContractStatus.PendingSignatures || c.Status == ContractStatus.Active));
        if (hasLiveContract)
            throw new BusinessRuleException(OfferContractConflictMessage);

        var land = offer.Land;
        if (land.Status != LandStatus.UnderContract)
            throw new BusinessRuleException("The land is not under contract.");

        var reservation = await _context.GridCapacityReservations
            .FirstOrDefaultAsync(r => r.OfferId == offer.Id && r.ReleasedAt == null)
            ?? throw new BusinessRuleException("This offer has no active grid reservation.");

        // Step 7 F1: an expired hold cannot become a contract. An active reservation is
        // ReleasedAt IS NULL and (ExpiresAt IS NULL or ExpiresAt in the future).
        if (reservation.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("The grid reservation for this offer has expired.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var contract = new Contract
        {
            OfferId = offer.Id,
            LandId = land.Id,
            InvestorId = offer.InvestorId,
            LandlordId = land.LandlordId,
            OfferVersionId = offer.AcceptedVersionId,
            CriterionId = land.VerifiedAgainstCriterionId,
            Status = ContractStatus.PendingSignatures,
            CreatedAt = DateTime.UtcNow
            // NoticePeriodDays is intentionally left to its database default (90).
        };

        _context.Contracts.Add(contract);
        await SaveContractChangesAsync();

        reservation.ContractId = contract.Id;
        await _context.SaveChangesAsync();

        // Established offer-closure path: Accepted -> Closed with a ClosedByContract history row.
        await _offerService.MarkOfferClosedAsync(offer.Id, contract.Id, adminId);

        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> SignAsync(int id)
    {
        var userId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent sign
        // requests for the same contract cannot both act on stale state.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);

        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new ForbiddenException("Only the contract parties can sign.");

        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only contracts pending signatures can be signed.");

        var now = DateTime.UtcNow;
        if (userId == contract.InvestorId)
        {
            if (contract.InvestorSignedAt is not null)
                throw new BusinessRuleException("The investor has already signed this contract.");
            contract.InvestorSignedAt = now;
        }
        else
        {
            if (contract.LandlordSignedAt is not null)
                throw new BusinessRuleException("The landlord has already signed this contract.");
            contract.LandlordSignedAt = now;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> ReviewAsync(int id, ReviewContractRequest request)
    {
        EnsureAdmin("Only admins can review contracts.");
        var adminId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent
        // review/terminate requests serialize and cannot insert conflicting review rows.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();

        if (contract.Status != ContractStatus.PendingSignatures)
            throw new BusinessRuleException("Only contracts pending signatures can be reviewed.");

        var now = DateTime.UtcNow;

        if (request.Approve)
        {
            // Step 7 F2: approval still requires both parties to have signed.
            if (contract.InvestorSignedAt is null || contract.LandlordSignedAt is null)
                throw new BusinessRuleException("Both parties must sign before the contract can be reviewed.");

            contract.Status = ContractStatus.Active;
            contract.AdminReviewedAt = now;
            contract.AdminSignedAt = now;
            AddReview(contract.Id, adminId, ContractReviewDecisions.Approved, null, now);

            // Step 7 F1: an approved contract's reservation becomes permanent (no hold expiry).
            await ClearReservationExpiryAsync(contract);

            // BR-O6 / state machine: once the contract is Active the project is live, so the
            // competing offers on the land are closed (they were only frozen while the land was
            // UnderContract). Closed offers keep their history and are never resumed.
            await CloseCompetitorOffersAsync(contract, adminId, now);
        }
        else
        {
            // Step 7 F2: rejection requires a reason but NOT signatures, so an unsigned
            // contract can still be rejected (releasing the hold and returning the land).
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationFailedException("A rejection reason is required.");

            contract.Status = ContractStatus.Rejected;
            AddReview(contract.Id, adminId, ContractReviewDecisions.Rejected, request.Reason, now);
            await ReleaseReservationAsync(contract, request.Reason!, now);
            ReturnLandToVerified(contract.Land, adminId, "Land returned to Verified after the contract was rejected.", now);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<ContractDto> TerminateAsync(int id, TerminateContractRequest request)
    {
        EnsureAdmin("Only admins can terminate contracts.");
        var adminId = RequireUserId();

        // Step 6 M-1: lock + re-read the contract inside the transaction so concurrent
        // review/terminate requests serialize and cannot both transition the same contract.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var contract = await LockContractAsync(id);
        await _context.Entry(contract).Reference(c => c.Land).LoadAsync();

        if (contract.Status != ContractStatus.Active)
            throw new BusinessRuleException("Only active contracts can be terminated.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("A termination reason is required.");

        var now = DateTime.UtcNow;

        contract.Status = ContractStatus.Terminated;
        AddReview(contract.Id, adminId, ContractReviewDecisions.Terminated, request.Reason, now);
        await ReleaseReservationAsync(contract, request.Reason, now);
        ReturnLandToVerified(contract.Land, adminId, "Land returned to Verified after the contract was terminated.", now);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoByIdAsync(contract.Id);
    }

    public async Task<List<ContractReviewDto>> GetReviewsAsync(int id)
    {
        var contract = await LoadContractAsync(id);
        EnsureCanView(contract);

        var reviews = await _context.ContractReviews
            .AsNoTracking()
            .Include(r => r.Reviewer)
            .Where(r => r.ContractId == id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        return reviews.Select(r => new ContractReviewDto
        {
            Id = r.Id,
            Decision = r.Decision,
            Reason = r.Reason,
            ReviewerName = r.Reviewer?.FullName ?? string.Empty,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    private bool IsAdmin => _currentUser.Role is RoleNames.Admin or RoleNames.SuperAdmin;

    private void EnsureAdmin(string message)
    {
        if (!IsAdmin)
            throw new ForbiddenException(message);
    }

    private int RequireUserId() =>
        _currentUser.Id ?? throw new ForbiddenException("Authentication is required.");

    private async Task<Contract> LoadContractAsync(int id)
    {
        return await _context.Contracts
            .Include(c => c.Land)
            .Include(c => c.Investor)
            .Include(c => c.Landlord)
            .Include(c => c.OfferVersion)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException("Contract not found.");
    }

    // Step 6 M-1: take an update lock on the contract row and re-read the current persisted
    // state. Callers invoke this inside a transaction, so concurrent sign/review/terminate
    // requests for the same contract serialize on this lock and each acts on fresh state.
    private async Task<Contract> LockContractAsync(int id)
    {
        return await _context.Contracts
            .FromSqlInterpolated($"SELECT * FROM CONTRACTS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {id}")
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Contract not found.");
    }

    private void EnsureCanView(Contract contract)
    {
        var userId = RequireUserId();
        if (IsAdmin || contract.InvestorId == userId || contract.LandlordId == userId)
            return;
        throw new ForbiddenException("You are not authorized to view this contract.");
    }

    private static ContractStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        // Strict: only actual ContractStatus names are accepted (numbers are rejected).
        var match = Enum.GetNames<ContractStatus>()
            .FirstOrDefault(name => string.Equals(name, status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationFailedException($"Unknown contract status: {status}");

        return Enum.Parse<ContractStatus>(match);
    }

    private void AddReview(int contractId, int reviewerId, string decision, string? reason, DateTime now)
    {
        _context.ContractReviews.Add(new ContractReview
        {
            ContractId = contractId,
            ReviewerId = reviewerId,
            Decision = decision,
            Reason = reason,
            CreatedAt = now
        });
    }

    /// <summary>
    /// BR-O6 / state machine: when a contract becomes Active, every non-terminal competing
    /// offer on the same land is closed with a <c>CompetitorClosed</c> history row. Terminal
    /// statuses (already accepted/rejected/withdrawn/closed) are left untouched, and the
    /// contract's own offer is excluded.
    /// </summary>
    private async Task CloseCompetitorOffersAsync(Contract contract, int actorId, DateTime now)
    {
        var competitors = await _context.Offers
            .Where(o => o.LandId == contract.LandId
                        && o.Id != contract.OfferId
                        && o.Status != OfferStatus.Accepted
                        && o.Status != OfferStatus.RejectedByLandlord
                        && o.Status != OfferStatus.RejectedByInvestor
                        && o.Status != OfferStatus.RejectedByAdmin
                        && o.Status != OfferStatus.WithdrawnByInvestor
                        && o.Status != OfferStatus.Closed)
            .ToListAsync();

        foreach (var offer in competitors)
        {
            offer.Status = OfferStatus.Closed;
            offer.UpdatedAt = now;
            _context.OfferNegotiationHistories.Add(new OfferNegotiationHistory
            {
                OfferId = offer.Id,
                ActionType = OfferHistoryActionType.CompetitorClosed,
                ActorId = actorId,
                Amount = offer.CurrentAmount,
                Message = "Competing offer closed when the contract became active.",
                CreatedAt = now
            });
        }
    }

    /// <summary>
    /// Releases the reservation linked to this contract (never deletes it). No-op if the
    /// reservation is missing or already released.
    /// </summary>
    private async Task ReleaseReservationAsync(Contract contract, string reason, DateTime now)
    {
        var reservation = await _context.GridCapacityReservations
            .FirstOrDefaultAsync(r => r.ContractId == contract.Id);
        if (reservation is null || reservation.ReleasedAt is not null)
            return;

        reservation.ReleasedAt = now;
        reservation.ReleaseReason = reason.Length <= ReleaseReasonMaxLength
            ? reason
            : reason[..ReleaseReasonMaxLength];
    }

    /// <summary>
    /// Step 7 F1: clears the linked reservation's hold expiry when its contract is approved,
    /// so an Active contract's reservation no longer expires. No-op if the reservation is missing.
    /// </summary>
    private async Task ClearReservationExpiryAsync(Contract contract)
    {
        var reservation = await _context.GridCapacityReservations
            .FirstOrDefaultAsync(r => r.ContractId == contract.Id);
        if (reservation is null)
            return;

        reservation.ExpiresAt = null;
    }

    /// <summary>Returns the land to Verified and appends one land-status history row.</summary>
    private void ReturnLandToVerified(Land land, int actorId, string reason, DateTime now)
    {
        if (land.Status == LandStatus.Verified)
            return; // already Verified: avoid a duplicate history row

        land.Status = LandStatus.Verified;
        _context.LandStatusHistories.Add(new LandStatusHistory
        {
            LandId = land.Id,
            Status = LandStatus.Verified,
            ChangedById = actorId,
            Reason = reason,
            ChangedAt = now
        });
    }

    private async Task SaveContractChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ__CONTRACT__96E0508AA6D26B86"))
        {
            throw new BusinessRuleException(OfferContractConflictMessage);
        }
    }

    // Only the CONTRACTS unique index on OFFER_ID is translated to 409; any other database
    // failure keeps the generic error path.
    private static bool IsUniqueViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is SqlException sql &&
        (sql.Number == 2601 || sql.Number == 2627) &&
        sql.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);

    private async Task<ContractDto> ToDtoByIdAsync(int id) => ToDto(await LoadContractAsync(id));

    private static ContractDto ToDto(Contract contract)
    {
        var version = contract.OfferVersion;
        return new ContractDto
        {
            Id = contract.Id,
            OfferId = contract.OfferId,
            LandId = contract.LandId,
            LandLabel = BuildLandLabel(contract.Land),
            InvestorId = contract.InvestorId,
            InvestorName = contract.Investor?.FullName ?? string.Empty,
            LandlordId = contract.LandlordId,
            LandlordName = contract.Landlord?.FullName ?? string.Empty,
            Status = contract.Status.ToString(),
            OfferVersionId = contract.OfferVersionId,
            CriterionId = contract.CriterionId,
            CreatedAt = contract.CreatedAt,
            InvestorSignedAt = contract.InvestorSignedAt,
            LandlordSignedAt = contract.LandlordSignedAt,
            AdminReviewedAt = contract.AdminReviewedAt,
            AdminSignedAt = contract.AdminSignedAt,
            LandlordSharePct = version?.LandlordSharePct,
            DurationYears = version?.DurationYears,
            StartDate = version?.StartDate,
            InstallationCost = version?.InstallationCost,
            SolarCellCapacityKw = version?.SolarCellCapacityKw,
            ExpectedAnnualRevenue = version?.ExpectedAnnualRevenue,
            EffectiveCostPerKw = version?.EffectiveCostPerKw,
            PaybackPeriodMonths = version?.PaybackPeriodMonths
        };
    }

    private static string BuildLandLabel(Land? land)
    {
        if (land is null)
            return string.Empty;

        var parts = new[] { land.Village, land.Basin, land.ParcelNumber }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" / ", parts);
    }
}
