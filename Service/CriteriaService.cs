using System.Data;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class CriteriaService(NebrasdbContext context, ICurrentUser currentUser, TimeProvider? timeProvider = null) : ICriteriaService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<LandCriterionDto> GetCurrentAsync()
    {
        var row = await context.LandCriteria.AsNoTracking().Include(c => c.UpdatedBy)
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync()
            ?? throw new NotFoundException("No eligibility criteria are configured.");
        var previous = row.PreviousCriterionId is int id
            ? await context.LandCriteria.AsNoTracking().SingleAsync(c => c.Id == id) : null;
        return ToDto(row, previous);
    }

    public async Task<List<LandCriterionDto>> GetHistoryAsync()
    {
        if (currentUser.Role is not (RoleNames.Admin or RoleNames.SuperAdmin))
            throw new ForbiddenException("Criteria history is restricted to administrators.");
        var rows = await context.LandCriteria.AsNoTracking().Include(c => c.UpdatedBy)
            .OrderByDescending(c => c.Id).ToListAsync();
        var byId = rows.ToDictionary(c => c.Id);
        return rows.Select(c => ToDto(c, c.PreviousCriterionId is int id ? byId.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<LandCriterionDto> CreateAsync(CreateLandCriterionRequest request)
    {
        // Enforce this at the service boundary too; actor IDs never come from a request DTO.
        if (currentUser.Role != RoleNames.SuperAdmin || currentUser.Id is not int actorId)
            throw new ForbiddenException("Only SuperAdmin may change eligibility criteria.");
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request,
            new System.ComponentModel.DataAnnotations.ValidationContext(request), errors, true)
            || string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException("Criteria values and a reason are required.");

        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await context.Users.AsNoTracking().Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == actorId && !u.IsDeleted);
        if (actor?.Role.Name != RoleNames.SuperAdmin)
            throw new ForbiddenException("Only an active SuperAdmin may change eligibility criteria.");
        // Serialize appenders, including the empty-table key range. ExpectedCurrentId also
        // rejects a form opened before another successful write rather than silently replacing it.
        var previous = await context.LandCriteria
            .FromSqlRaw("SELECT * FROM [LAND_CRITERIA] WITH (UPDLOCK, HOLDLOCK)")
            .AsNoTracking().OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        EnsureExpectedCurrent(request.ExpectedCurrentId, previous?.Id);
        var row = new LandCriterion {
            MinAreaDonum=request.MinAreaDonum, MaxSlopePct=request.MaxSlopePct,
            MaxGridDistanceKm=request.MaxGridDistanceKm, MinSolarIrradiance=request.MinSolarIrradiance,
            MinElevationM=request.MinElevationM, Reason=request.Reason.Trim(),
            PreviousCriterionId=previous?.Id, UpdatedById=actorId,
            UpdatedByNameSnapshot=actor.FullName, UpdatedByRoleSnapshot=actor.Role.Name,
            UpdatedAt=clock.GetUtcNow().UtcDateTime
        };
        context.LandCriteria.Add(row);
        await context.SaveChangesAsync();
        await tx.CommitAsync();
        row.UpdatedBy=actor;
        return ToDto(row, previous);
    }

    public static void EnsureExpectedCurrent(int? expectedId, int? actualId)
    {
        if (expectedId != (actualId ?? 0))
            throw new BusinessRuleException("Criteria changed. Refresh the current record before submitting again.");
    }

    public static LandCriterionDto ToDto(LandCriterion row, LandCriterion? previous) => new(
        row.Id, row.MinAreaDonum, row.MaxSlopePct, row.MaxGridDistanceKm,
        row.MinSolarIrradiance, row.MinElevationM, row.UpdatedById,
        row.UpdatedByNameSnapshot ?? row.UpdatedBy?.FullName ?? "",
        row.UpdatedAt, row.Reason, row.PreviousCriterionId, row.UpdatedByRoleSnapshot,
        row.UpdatedByNameSnapshot is not null && row.UpdatedByRoleSnapshot is not null,
        previous is null ? null : new CriterionValuesDto(previous.Id, previous.MinAreaDonum,
            previous.MaxSlopePct, previous.MaxGridDistanceKm, previous.MinSolarIrradiance, previous.MinElevationM),
        new[] {
            new CriterionFieldChangeDto("minAreaDonum", previous?.MinAreaDonum, row.MinAreaDonum),
            new CriterionFieldChangeDto("maxSlopePct", previous?.MaxSlopePct, row.MaxSlopePct),
            new CriterionFieldChangeDto("maxGridDistanceKm", previous?.MaxGridDistanceKm, row.MaxGridDistanceKm),
            new CriterionFieldChangeDto("minSolarIrradiance", previous?.MinSolarIrradiance, row.MinSolarIrradiance),
            new CriterionFieldChangeDto("minElevationM", previous?.MinElevationM, row.MinElevationM)
        });
}
