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
        // 1. Offer موجود وحالته Accepted
        var offer = await _context.Offers
            .Include(o => o.OfferVersions)
            .Include(o => o.Land)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId);
        if (offer == null)
            throw new KeyNotFoundException("Offer not found.");

        var acceptedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Accepted");
        if (offer.StatusId != acceptedStatus.Id)
            throw new InvalidOperationException("Offer is not in 'Accepted' status.");

        // 2. Offer ما عنده Contract من قبل
        var existingContract = await _context.Contracts.AnyAsync(c => c.OfferId == request.OfferId);
        if (existingContract)
            throw new InvalidOperationException("A contract already exists for this offer.");

        // 3. المستثمر غير المالك (Self-dealing)
        if (offer.InvestorId == offer.Land.LandlordId)
            throw new InvalidOperationException("Investor cannot be the same as the landlord.");

        // 4. الأرض ما عقدها عقد Active آخر
        var hasActiveContract = await _context.Contracts.AnyAsync(c =>
            c.LandId == offer.LandId && c.StatusId == 2);
        if (hasActiveContract)
            throw new InvalidOperationException("Land already has an active contract.");

        // 5. إنشاء العقد
        var pendingStatus = await _context.ContractStatuses.FirstAsync(s => s.Name == "PendingSignatures");

        var contract = new Contract
        {
            OfferId = offer.Id,
            LandId = offer.LandId,
            InvestorId = offer.InvestorId,
            LandlordId = offer.Land.LandlordId,
            StatusId = pendingStatus.Id,
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
            // التحقق من التوقيعات
            if (contract.InvestorSignedAt == null)
                throw new InvalidOperationException("Investor has not signed yet.");
            if (contract.LandlordSignedAt == null)
                throw new InvalidOperationException("Landlord has not signed yet.");

            // التحقق من معايير الأرض
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

            // التحقق من السعة الكهربائية
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

            // الموافقة
            contract.AdminReviewedAt = DateTime.UtcNow;
            contract.AdminSignedAt = DateTime.UtcNow;

            var activeStatus = await _context.ContractStatuses.FirstAsync(s => s.Name == "Active");
            contract.StatusId = activeStatus.Id;

            // حجز السعة
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
            var rejectedStatus = await _context.ContractStatuses.FirstAsync(s => s.Name == "Rejected");
            contract.StatusId = rejectedStatus.Id;
        }
        else
        {
            throw new InvalidOperationException("Decision must be 'Approved' or 'Rejected'.");
        }

        await _context.SaveChangesAsync();
        return review.Adapt<ContractReviewDto>();
    }

    public async Task<bool> TerminateAsync(int contractId, int adminId, string reason)
    {
        var contract = await _context.Contracts
            .Include(c => c.Land)
            .Include(c => c.GridCapacityReservation)
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract == null)
            throw new KeyNotFoundException("Contract not found.");

        if (contract.StatusId != 2) // Active
            throw new InvalidOperationException("Only active contracts can be terminated.");

        var terminatedStatus = await _context.ContractStatuses.FirstAsync(s => s.Name == "Terminated");
        contract.StatusId = terminatedStatus.Id;

        // تحرير حجز السعة
        if (contract.GridCapacityReservation != null)
        {
            _context.GridCapacityReservations.Remove(contract.GridCapacityReservation);
        }

        // إعادة حالة الأرض
        var verifiedStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Verified");
        contract.Land.LandStatusId = verifiedStatus.Id;

        // تسجيل المراجعة
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
