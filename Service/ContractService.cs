using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class ContractService : IContractService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<ContractService> _logger;

    public ContractService(NebrasdbContext context, ILogger<ContractService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ContractDto>> GetAllAsync()
    {
        var contracts = await _context.Contracts.ToListAsync();
        var result = new List<ContractDto>();
        foreach (var c in contracts) result.Add(c.Adapt<ContractDto>());
        return result;
    }

    public async Task<ContractDto?> GetByIdAsync(int id)
    {
        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return null;
        return contract.Adapt<ContractDto>();
    }

    public async Task<ContractDto> CreateAsync(CreateContractRequest request)
    {
        var offer = await _context.Offers
            .Include(o => o.OfferVersions)
            .Include(o => o.Land)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId);
        if (offer == null)
            throw new KeyNotFoundException("Offer not found.");

        var acceptedStatus = await _context.OfferStatuses
            .FirstAsync(s => s.NameStatus == "Accepted");
        if (offer.StatusId != acceptedStatus.Id)
            throw new InvalidOperationException("Offer is not in 'Accepted' status.");

        var existingContract = await _context.Contracts
            .AnyAsync(c => c.OfferId == request.OfferId);
        if (existingContract)
            throw new InvalidOperationException("A contract already exists for this offer.");

        if (offer.InvestorId == offer.Land.LandlordId)
            throw new InvalidOperationException("Investor cannot be the same as the landlord.");

        var hasActiveContract = await _context.Contracts.AnyAsync(c =>
            c.LandId == offer.LandId && c.StatusId == 2);
        if (hasActiveContract)
            throw new InvalidOperationException("Land already has an active contract.");

        // تحديد OfferVersionId من الإصدار المقبول
        var lastVersion = await _context.OfferVersions
            .Where(v => v.OfferId == offer.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        var pendingStatus = await _context.ContractStatuses
            .FirstAsync(s => s.Name == ContractStatusNames.PendingSignatures);

        var contract = new Contract
        {
            OfferId = offer.Id,
            LandId = offer.LandId,
            InvestorId = offer.InvestorId,
            LandlordId = offer.Land.LandlordId,
            StatusId = pendingStatus.Id,
            OfferVersionId = lastVersion?.Id,
            NoticePeriodDays = 90,
            CreatedAt = DateTime.UtcNow
        };

        _context.Contracts.Add(contract);
        await _context.SaveChangesAsync();

        return contract.Adapt<ContractDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateContractRequest request)
    {
        throw new InvalidOperationException("Direct update of contract is not allowed. Use sign/review methods instead.");
    }

    public async Task<bool> DeleteAsync(int id)
    {
        throw new InvalidOperationException("Direct deletion is not allowed. Use TerminateAsync instead.");
    }

    public async Task<bool> SignAsInvestorAsync(int contractId, int userId)
    {
        var contract = await _context.Contracts.FindAsync(contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        if (contract.InvestorId != userId)
            throw new UnauthorizedAccessException("Only the contract investor can sign.");

        if (contract.InvestorSignedAt != null)
            throw new InvalidOperationException("Investor has already signed.");

        contract.InvestorSignedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SignAsLandlordAsync(int contractId, int userId)
    {
        var contract = await _context.Contracts.FindAsync(contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        if (contract.LandlordId != userId)
            throw new UnauthorizedAccessException("Only the contract landlord can sign.");

        if (contract.LandlordSignedAt != null)
            throw new InvalidOperationException("Landlord has already signed.");

        contract.LandlordSignedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<ContractReviewDto> AdminReviewAsync(int contractId, int adminId, string decision, string? reason)
    {
        var contract = await _context.Contracts
            .Include(c => c.Offer).ThenInclude(o => o.OfferVersions)
            .Include(c => c.Land)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        var review = new ContractReview
        {
            ContractId = contractId,
            ReviewerId = adminId,
            Decision = decision,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContractReviews.Add(review);

        if (decision == "Approved")
        {
            if (contract.InvestorSignedAt == null)
                throw new InvalidOperationException("Investor has not signed yet.");
            if (contract.LandlordSignedAt == null)
                throw new InvalidOperationException("Landlord has not signed yet.");

            var criteria = await _context.LandCriteria
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync();
            if (criteria != null)
            {
                var land = contract.Land;
                if (land.AreaDonum < criteria.MinAreaDonum ||
                    land.SlopePercentage > criteria.MaxSlopePct ||
                    land.DistanceToGridKm > criteria.MaxGridDistanceKm ||
                    land.SolarIrradiance < criteria.MinSolarIrradiance ||
                    land.ElevationM < criteria.MinElevationM)
                    throw new InvalidOperationException("Land no longer meets eligibility criteria.");
            }

            var grid = await _context.Grids
                .Include(g => g.GridCapacityReservations)
                .FirstOrDefaultAsync(g => g.Region.Lands.Any(l => l.Id == contract.LandId));

            if (grid == null)
                throw new InvalidOperationException("No grid found for this land's region.");

            var usedMw = grid.GridCapacityReservations.Sum(r => r.ReservedMw);
            var availableMw = grid.CapacityMw - usedMw;
            if (contract.Offer.RequiredCapacityMw > availableMw)
                throw new InvalidOperationException(
                    $"Insufficient grid capacity. Available: {availableMw} MW, Required: {contract.Offer.RequiredCapacityMw} MW.");

            contract.AdminReviewedAt = DateTime.UtcNow;
            contract.AdminSignedAt = DateTime.UtcNow;

            var activeStatus = await _context.ContractStatuses
                .FirstAsync(s => s.Name == ContractStatusNames.Active);
            contract.StatusId = activeStatus.Id;

            // تعيين AcceptedVersionId على الـ Offer (يثبت الإصدار)
            var latestVersion = await _context.OfferVersions
                .Where(v => v.OfferId == contract.OfferId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();
            if (latestVersion != null)
            {
                contract.Offer.AcceptedVersionId = latestVersion.Id;
            }

            _context.GridCapacityReservations.Add(new GridCapacityReservation
            {
                GridId = grid.Id,
                ContractId = contract.Id,
                ReservedMw = contract.Offer.RequiredCapacityMw,
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (decision == "Rejected")
        {
            var rejectedStatus = await _context.ContractStatuses
                .FirstAsync(s => s.Name == ContractStatusNames.Rejected);
            contract.StatusId = rejectedStatus.Id;
        }
        else
        {
            throw new InvalidOperationException("Decision must be 'Approved' or 'Rejected'.");
        }

        await _context.SaveChangesAsync();
        return review.Adapt<ContractReviewDto>();
    }

    // ============ CANCELLATION FLOW ============

    public async Task<bool> RequestCancellationAsync(int contractId, int userId, string reason, decimal? investorPenaltyAmount)
    {
        var contract = await _context.Contracts
            .Include(c => c.OfferVersion)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        var activeStatus = await _context.ContractStatuses
            .FirstAsync(s => s.Name == ContractStatusNames.Active);
        if (contract.StatusId != activeStatus.Id)
            throw new InvalidOperationException("Only active contracts can be cancelled.");

        if (contract.CancellationRequestedById != null)
            throw new InvalidOperationException("Cancellation already requested.");

        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new UnauthorizedAccessException("Only the investor or landlord can request cancellation.");

        if (contract.OfferVersion?.InstallationCost == null)
            throw new InvalidOperationException(
                "Cannot process cancellation: installation cost has not been recorded for this contract.");

        if (userId == contract.InvestorId)
        {
            // Investor يطلب الإلغاء → InvestorPenaltyAmount إجباري
            if (investorPenaltyAmount == null || investorPenaltyAmount <= 0)
                throw new InvalidOperationException("Investor must provide a penalty amount for cancellation.");

            // الحد الأدنى للتعويض = (InstallationCost / DurationYears) × السنوات الباقية (pro-rated)
            if (contract.OfferVersion.DurationYears == null || contract.OfferVersion.DurationYears <= 0)
                throw new InvalidOperationException("Contract duration is not set.");

            if (contract.OfferVersion.StartDate == null)
                throw new InvalidOperationException("Contract start date is not set.");

            var termEnd = contract.OfferVersion.StartDate.Value.AddYears(contract.OfferVersion.DurationYears.Value);
            var cancellationDate = DateTime.UtcNow.AddDays(contract.NoticePeriodDays > 0 ? contract.NoticePeriodDays : 90);
            var remainingDays = Math.Max(0, (termEnd.ToDateTime(TimeOnly.MinValue) - cancellationDate).TotalDays);
            var remainingYears = (decimal)remainingDays / 365.25m;
            var minPenalty = (contract.OfferVersion.InstallationCost.Value / contract.OfferVersion.DurationYears.Value) * remainingYears;

            if (investorPenaltyAmount < minPenalty)
                throw new InvalidOperationException(
                    $"Penalty must be at least {Math.Round(minPenalty, 3)} (unamortized installation cost).");

            contract.InvestorPenaltyAmount = investorPenaltyAmount;
            contract.CompensationAmount = investorPenaltyAmount;
        }
        else
        {
            // Landlord يطلب الإلغاء → تعويض للمستثمر = InstallationCost كاملة
            contract.CompensationAmount = contract.OfferVersion.InstallationCost;
        }

        contract.CancellationRequestedById = userId;
        contract.CancellationRequestedAt = DateTime.UtcNow;
        contract.CancellationEffectiveDate = DateTime.UtcNow.AddDays(
            contract.NoticePeriodDays > 0 ? contract.NoticePeriodDays : 90);

        _context.ContractReviews.Add(new ContractReview
        {
            ContractId = contractId,
            ReviewerId = userId,
            Decision = "CancellationRequested",
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RespondToCancellationAsync(int contractId, int userId, bool agree)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        if (contract.CancellationRequestedById == null)
            throw new InvalidOperationException("No cancellation request is pending.");

        if (contract.DisputeFlagged)
            throw new InvalidOperationException("Cancellation is already in dispute.");

        // الطرف الآخر فقط يرد
        if (userId != contract.InvestorId && userId != contract.LandlordId)
            throw new UnauthorizedAccessException("Only the investor or landlord can respond.");

        if (userId == contract.CancellationRequestedById)
            throw new InvalidOperationException("Cannot respond to your own cancellation request.");

        if (agree)
        {
            _context.ContractReviews.Add(new ContractReview
            {
                ContractId = contractId,
                ReviewerId = userId,
                Decision = "CancellationAgreed",
                Reason = "Party agreed to cancellation. Termination pending effective date.",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            contract.DisputeFlagged = true;

            _context.ContractReviews.Add(new ContractReview
            {
                ContractId = contractId,
                ReviewerId = userId,
                Decision = "Dispute",
                Reason = "Legal counsel required - party disagrees with cancellation.",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AdminForceTerminateAsync(int contractId, int adminId, string justification, decimal? compensationOverride)
    {
        // TODO: replace with [Authorize(Roles="SuperAdmin")] once JWT auth is added
        var admin = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == adminId);
        if (admin == null || admin.Role.Name != RoleNames.SuperAdmin)
            throw new UnauthorizedAccessException("Only SuperAdmin can force-terminate contracts.");

        var contract = await _context.Contracts
            .Include(c => c.Land)
            .Include(c => c.GridCapacityReservation)
            .Include(c => c.OfferVersion)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        if (contract.StatusId != (await _context.ContractStatuses
                .FirstAsync(s => s.Name == ContractStatusNames.Active)).Id)
            throw new InvalidOperationException("Only active contracts can be terminated.");

        // يتجاوز DisputeFlagged guard و CancellationEffectiveDate guard
        var terminatedStatus = await _context.ContractStatuses
            .FirstAsync(s => s.Name == ContractStatusNames.Terminated);
        contract.StatusId = terminatedStatus.Id;

        if (contract.GridCapacityReservation != null)
        {
            _context.GridCapacityReservations.Remove(contract.GridCapacityReservation);
        }

        var verifiedStatus = await _context.LandStatuses
            .FirstAsync(s => s.Name == LandStatusNames.Verified);
        contract.Land.LandStatusId = verifiedStatus.Id;

        if (compensationOverride.HasValue)
        {
            if (compensationOverride < 0)
                throw new InvalidOperationException("Compensation override cannot be negative.");
            contract.CompensationAmount = compensationOverride;
        }

        _context.ContractReviews.Add(new ContractReview
        {
            ContractId = contractId,
            ReviewerId = adminId,
            Decision = "AdminForceTerminated",
            Reason = justification,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }

    // ============ TERMINATE ============

    public async Task<bool> TerminateAsync(int contractId, int adminId, string reason)
    {
        var contract = await _context.Contracts
            .Include(c => c.Land)
            .Include(c => c.GridCapacityReservation)
            .Include(c => c.OfferVersion)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        var activeStatus = await _context.ContractStatuses
            .FirstAsync(s => s.Name == ContractStatusNames.Active);
        if (contract.StatusId != activeStatus.Id)
            throw new InvalidOperationException("Only active contracts can be terminated.");

        // DisputeFlagged → لا يمكن الإنهاء إلا بقرار إداري (AdminForceTerminateAsync)
        if (contract.DisputeFlagged)
            throw new InvalidOperationException(
                "Cannot terminate a disputed contract. Use AdminForceTerminateAsync instead.");

        // إذا CancellationRequestedById != null → يجب أن يكون CancellationEffectiveDate قد مضى
        if (contract.CancellationRequestedById != null && !contract.DisputeFlagged)
        {
            if (DateTime.UtcNow < contract.CancellationEffectiveDate)
                throw new InvalidOperationException(
                    "Cannot terminate before cancellation effective date.");
        }

        var terminatedStatus = await _context.ContractStatuses
            .FirstAsync(s => s.Name == ContractStatusNames.Terminated);
        contract.StatusId = terminatedStatus.Id;

        if (contract.GridCapacityReservation != null)
        {
            _context.GridCapacityReservations.Remove(contract.GridCapacityReservation);
        }

        // إعادة حالة الأرض إلى Verified
        var verifiedStatus = await _context.LandStatuses
            .FirstAsync(s => s.Name == LandStatusNames.Verified);
        contract.Land.LandStatusId = verifiedStatus.Id;

        _context.ContractReviews.Add(new ContractReview
        {
            ContractId = contractId,
            ReviewerId = adminId,
            Decision = "Terminated",
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }
}
