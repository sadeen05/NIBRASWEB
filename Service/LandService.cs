using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

public class LandService : ILandService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandService> _logger;

    public LandService(NebrasdbContext context, ILogger<LandService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandDto>> GetAllAsync(int pageNumber, int pageSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 50) pageSize = 50;

        var lands = await _context.Lands
            .Include(l => l.Region)
            .Include(l => l.Landlord)
            .Where(l => !l.IsDeleted)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new List<LandDto>();
        foreach (var land in lands)
        {
            result.Add(ToDto(land));
        }

        return result;
    }

    public async Task<LandDto?> GetByIdAsync(int id)
    {
        var land = await _context.Lands
            .Include(l => l.Region)
            .Include(l => l.Landlord)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (land == null) return null;
        return ToDto(land);
    }

    public async Task<LandDto> CreateAsync(CreateLandRequest request)
    {
        var user = await _context.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.LandlordId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");
        if (user.Role.Name != "Landlord")
            throw new KeyNotFoundException("Only landlords can register land.");

        var exists = await _context.Lands.AnyAsync(l =>
            l.LandNumber == request.LandNumber && l.RegionId == request.RegionId && !l.IsDeleted);
        if (exists)
            throw new InvalidOperationException("Land number already exists in this region.");

        var criterion = await _context.LandCriteria
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        var standardErrors = ValidateRegisterStandards(request, criterion);
        if (standardErrors.Any())
            throw new InvalidOperationException($"Land does not meet registration standards: {string.Join(";", standardErrors)}");
        var grid = await _context.Grids
            .Include(g => g.GridCapacityReservations)
            .FirstOrDefaultAsync(g => g.Id == request.GridId);

        if (grid == null)
            throw new KeyNotFoundException("Grid not found for this region.");

        var totalReservedMw = grid.GridCapacityReservations.Sum(r => r.ReservedMw);
        if (totalReservedMw >= grid.CapacityMw)
            throw new InvalidOperationException("No capacity left in this electrical grid.");

        var land = request.Adapt<Land>();
        land.Status = LandStatus.Draft;
        land.DataVerifiedByAdmin = false;
        land.IsDeleted = false;
        land.GridId = request.GridId;

        _context.Add(land);
        await _context.SaveChangesAsync();

        await ChangeStatusAsync(land, LandStatus.Draft, request.LandlordId, "Land created as Draft.");

        return ToDto(land);
    }

    private List<string> ValidateRegisterStandards(CreateLandRequest request, LandCriterion? criterion)
    {
        var errors = new List<string>();
        if (request.AreaDonum <= 0)
            errors.Add("Land area must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.LandNumber))
            errors.Add("Land number is required.");
        if (request.RegionId <= 0)
            errors.Add("A valid RegionId is required.");

        if (criterion != null)
        {
            if (criterion.MinAreaDonum.HasValue && request.AreaDonum < criterion.MinAreaDonum.Value)
                errors.Add($"Land area is less than the minimum required ({criterion.MinAreaDonum} Donums).");
            if (criterion.MaxSlopePct.HasValue && request.SlopePercentage > criterion.MaxSlopePct.Value)
                errors.Add($"Land slope exceeds the maximum allowed limit ({criterion.MaxSlopePct}%).");
            if (criterion.MaxGridDistanceKm.HasValue && request.DistanceToGridKm > criterion.MaxGridDistanceKm.Value)
                errors.Add($"Land distance to grid exceeds the maximum allowed limit ({criterion.MaxGridDistanceKm} km).");
        }

        return errors;
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandRequest request)
    {
        var land = await _context.Lands
            .Include(l => l.Contracts)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);

        if (land == null) return false;
        if (land.Contracts.Any(c => c.Status == ContractStatus.Active))
            throw new InvalidOperationException("Cannot edit a land that has an active contract.");

        land.LandNumber = request.LandNumber;
        land.AreaDonum = request.AreaDonum;
        land.SlopePercentage = request.SlopePercentage;
        land.DistanceToGridKm = request.DistanceToGridKm;
        land.SolarIrradiance = request.SolarIrradiance;
        land.ElevationM = request.ElevationM;
        land.RegionId = request.RegionId;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        throw new InvalidOperationException("Direct deletion is not allowed.");
    }

    public async Task<bool> SubmitAsync(int landId, int landlordId)
    {
        var land = await _context.Lands
            .Include(l => l.LandDocuments)
            .FirstOrDefaultAsync(l => l.Id == landId && !l.IsDeleted);

        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        if (land.LandlordId != landlordId)
            throw new UnauthorizedAccessException("You are not the owner of this land.");

        if (land.Status != LandStatus.Draft)
            throw new InvalidOperationException("Only lands in Draft status can be submitted.");

        await ChangeStatusAsync(land, LandStatus.PendingVerification, landlordId, "Submitted for admin verification.");
        return true;
    }

    public async Task<bool> VerifyAsync(int landId, int adminId)
    {
        var land = await _context.Lands
            .Include(l => l.LandDocuments)
            .ThenInclude(ld => ld.DocumentType)
            .FirstOrDefaultAsync(l => l.Id == landId && !l.IsDeleted);

        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        if (land.Status != LandStatus.PendingVerification)
            throw new InvalidOperationException("Land is not pending verification.");

        var hasTitleDeed = land.LandDocuments.Any(d =>
            d.DocumentType.Name == "TitleDeed" && d.Status == "Approved");
        if (!hasTitleDeed)
            throw new InvalidOperationException("No approved title deed document found.");

        var criteria = await _context.LandCriteria
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();
        if (criteria == null)
            throw new InvalidOperationException("No eligibility criteria configured.");

        var eligibility = await CheckEligibilityAsync(landId);
        if (!eligibility)
            throw new InvalidOperationException("Land does not meet eligibility criteria.");

        land.VerifiedAgainstCriterionId = criteria.Id;

        land.DataVerifiedByAdmin = true;

        await ChangeStatusAsync(land, LandStatus.Verified, adminId, "Verified by admin");
        return true;
    }

    public async Task<bool> RejectAsync(int landId, int adminId, string reason)
    {
        var land = await _context.Lands.FindAsync(landId);
        if (land == null || land.IsDeleted)
            throw new KeyNotFoundException("Land not found.");

        if (land.Status != LandStatus.PendingVerification)
            throw new InvalidOperationException("Land is not pending verification.");

        await ChangeStatusAsync(land, LandStatus.Rejected, adminId, reason);
        return true;
    }

    public async Task<bool> CheckEligibilityAsync(int landId)
    {
        var land = await _context.Lands.FirstOrDefaultAsync(l => l.Id == landId);
        if (land == null) return false;

        var criteria = await _context.LandCriteria.OrderByDescending(c => c.UpdatedAt).FirstOrDefaultAsync();
        if (criteria == null) return true;

        if ((criteria.MinAreaDonum.HasValue && land.AreaDonum < criteria.MinAreaDonum.Value) ||
            (criteria.MaxSlopePct.HasValue && land.SlopePercentage > criteria.MaxSlopePct.Value) ||
            (criteria.MaxGridDistanceKm.HasValue && land.DistanceToGridKm > criteria.MaxGridDistanceKm.Value) ||
            (criteria.MinSolarIrradiance.HasValue && land.SolarIrradiance < criteria.MinSolarIrradiance.Value) ||
            (criteria.MinElevationM.HasValue && land.ElevationM < criteria.MinElevationM.Value))
        {
            return false;
        }

        return true;
    }

    public async Task<List<LandStatusHistoryDto>> GetAllStatusHistoryAsync(int landId)
    {
        var history = await _context.LandStatusHistories
            .Include(h => h.ChangedBy)
            .Where(h => h.LandId == landId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();

        var result = new List<LandStatusHistoryDto>();
        foreach (var h in history)
        {
            result.Add(new LandStatusHistoryDto
            {
                id = h.Id,
                LandId = h.LandId,
                statusName = h.Status.ToString(),
                ChangeByName = h.ChangedBy.FullName,
                Reason = h.Reason,
                ChangedAt = h.ChangedAt ?? DateTime.MinValue
            });
        }
        return result;
    }

    private async Task ChangeStatusAsync(Land land, LandStatus newStatus, int changedById, string? reason)
    {
        land.Status = newStatus;

        _context.LandStatusHistories.Add(new LandStatusHistory
        {
            LandId = land.Id,
            Status = newStatus,
            ChangedById = changedById,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private static LandDto ToDto(Land land)
    {
        var dto = land.Adapt<LandDto>();
        dto.Status = land.Status.ToString();
        return dto;
    }
}