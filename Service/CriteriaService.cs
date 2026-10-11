using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class CriteriaService : ICriteriaService
{
    private readonly NebrasdbContext _context;
    private readonly ICurrentUser _currentUser;

    public CriteriaService(NebrasdbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<LandCriterionDto> GetCurrentAsync()
    {
        var criterion = await _context.LandCriteria
            .AsNoTracking()
            .Include(c => c.UpdatedBy)
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("No eligibility criteria are configured.");

        return ToDto(criterion);
    }

    public async Task<List<LandCriterionDto>> GetHistoryAsync()
    {
        var criteria = await _context.LandCriteria
            .AsNoTracking()
            .Include(c => c.UpdatedBy)
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        return criteria.Select(ToDto).ToList();
    }

    public async Task<LandCriterionDto> CreateAsync(CreateLandCriterionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("A reason is required.");

        var userId = _currentUser.Id ?? throw new ForbiddenException("Authentication is required.");

        var criterion = new LandCriterion
        {
            MinAreaDonum = request.MinAreaDonum,
            MaxSlopePct = request.MaxSlopePct,
            MaxGridDistanceKm = request.MaxGridDistanceKm,
            MinSolarIrradiance = request.MinSolarIrradiance,
            MinElevationM = request.MinElevationM,
            UpdatedById = userId,
            UpdatedAt = DateTime.UtcNow,
            Reason = request.Reason
        };

        _context.LandCriteria.Add(criterion);
        await _context.SaveChangesAsync();
        await _context.Entry(criterion).Reference(c => c.UpdatedBy).LoadAsync();

        return ToDto(criterion);
    }

    private static LandCriterionDto ToDto(LandCriterion criterion) => new(
        criterion.Id,
        criterion.MinAreaDonum,
        criterion.MaxSlopePct,
        criterion.MaxGridDistanceKm,
        criterion.MinSolarIrradiance,
        criterion.MinElevationM,
        criterion.UpdatedById,
        criterion.UpdatedBy.FullName,
        criterion.UpdatedAt,
        criterion.Reason);
}
