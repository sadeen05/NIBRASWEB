using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

public class LandService : ILandService
{
    private const int MaxPageSize = 50;
    private const string LandNumberConflictMessage = "A land with this land number already exists in the selected region.";
    private const string LandLocationConflictMessage = "A land with the same region, village, basin and parcel number already exists.";
    private readonly NebrasdbContext _context;
    private readonly ICurrentUser _currentUser;

    public LandService(NebrasdbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<LandListResponse> GetAllAsync(int pageNumber, int pageSize, string? status)
    {
        if (pageNumber < 1)
            throw new ValidationFailedException("Page number must be at least 1.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationFailedException("Page size must be between 1 and 50.");

        var statusFilter = ParseStatus(status);

        var query = _context.Lands
            .AsNoTracking()
            .Include(l => l.Region)
            .Where(l => !l.IsDeleted);
        query = await ApplyVisibilityAsync(query);

        if (statusFilter is { } filter)
            query = query.Where(l => l.Status == filter);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // SEC-14 / 4.5: guard the offset arithmetic against Int32 overflow. A page
        // number large enough that (pageNumber-1)*pageSize exceeds Int32 cannot exist in
        // any result set, so it yields an empty page instead of a negative OFFSET, which
        // SQL Server rejects as an error (and would surface as a generic 500).
        var offset = (long)(pageNumber - 1) * pageSize;
        if (offset > int.MaxValue)
            return new LandListResponse([], pageNumber, pageSize, totalCount, totalPages);

        var lands = await query
            .OrderBy(l => l.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync();

        return new LandListResponse(lands.Select(ToDto).ToList(), pageNumber, pageSize, totalCount, totalPages);
    }

    public async Task<LandDto> GetByIdAsync(int id)
    {
        var land = await LoadLandAsync(id);
        await EnsureCanViewAsync(land);
        return ToDto(land);
    }

    public async Task<LandDto> CreateAsync(CreateLandRequest request)
    {
        if (_currentUser.Role != RoleNames.Landlord)
            throw new ForbiddenException("Only landlords can register land.");

        var userId = RequireUserId();

        await EnsureGridBelongsToRegionAsync(request.GridId, request.RegionId);
        await EnsureNoDuplicateParcelAsync(request.RegionId, request.Village, request.Basin, request.ParcelNumber, null);
        await EnsureLandNumberAvailableAsync(request.RegionId, request.LandNumber, null);

        var results = EvaluateEligibility(
            request.AreaDonum, request.SlopePercentage, request.DistanceToGridKm,
            request.SolarIrradiance, request.ElevationM, await GetCurrentCriterionAsync());
        EnsureEligible(results);

        var land = new Land
        {
            LandlordId = userId,
            RegionId = request.RegionId,
            GridId = request.GridId,
            LandNumber = request.LandNumber,
            Village = request.Village,
            Basin = request.Basin,
            ParcelNumber = request.ParcelNumber,
            AreaDonum = request.AreaDonum,
            SlopePercentage = request.SlopePercentage,
            DistanceToGridKm = request.DistanceToGridKm,
            SolarIrradiance = request.SolarIrradiance,
            ElevationM = request.ElevationM,
            Status = LandStatus.Draft,
            DataVerifiedByAdmin = false,
            IsDeleted = false
        };

        await using var transaction = await _context.Database.BeginTransactionAsync();
        _context.Lands.Add(land);
        await SaveLandChangesAsync();

        _context.LandStatusHistories.Add(NewHistory(land.Id, LandStatus.Draft, userId, "Land created as Draft."));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToDtoAsync(land);
    }

    public async Task UpdateAsync(int id, UpdateLandRequest request)
    {
        var land = await _context.Lands
            .Include(l => l.Contracts)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        EnsureOwner(land);

        if (land.Status is not (LandStatus.Draft or LandStatus.Rejected))
            throw new BusinessRuleException("Only lands in Draft or Rejected status can be edited.");

        if (land.Contracts.Any(c => c.Status == ContractStatus.Active))
            throw new BusinessRuleException("Cannot edit a land that has an active contract.");

        if (request.RegionId != land.RegionId && request.GridId == land.GridId)
            throw new ValidationFailedException("Changing the region requires a grid in the new region.");

        await EnsureGridBelongsToRegionAsync(request.GridId, request.RegionId);
        await EnsureNoDuplicateParcelAsync(request.RegionId, request.Village, request.Basin, request.ParcelNumber, id);
        await EnsureLandNumberAvailableAsync(request.RegionId, request.LandNumber, id);

        var results = EvaluateEligibility(
            request.AreaDonum, request.SlopePercentage, request.DistanceToGridKm,
            request.SolarIrradiance, request.ElevationM, await GetCurrentCriterionAsync());
        EnsureEligible(results);

        land.RegionId = request.RegionId;
        land.GridId = request.GridId;
        land.LandNumber = request.LandNumber;
        land.Village = request.Village;
        land.Basin = request.Basin;
        land.ParcelNumber = request.ParcelNumber;
        land.AreaDonum = request.AreaDonum;
        land.SlopePercentage = request.SlopePercentage;
        land.DistanceToGridKm = request.DistanceToGridKm;
        land.SolarIrradiance = request.SolarIrradiance;
        land.ElevationM = request.ElevationM;

        await SaveLandChangesAsync();
    }

    public async Task SubmitAsync(int id)
    {
        var land = await LoadLandAsync(id);
        EnsureOwner(land);

        if (land.Status != LandStatus.Draft)
            throw new BusinessRuleException("Only lands in Draft status can be submitted.");

        if (string.IsNullOrWhiteSpace(land.Village) ||
            string.IsNullOrWhiteSpace(land.Basin) ||
            string.IsNullOrWhiteSpace(land.ParcelNumber))
            throw new BusinessRuleException("Village, basin and parcel number are required before submission.");

        var hasRequiredDocument = await _context.LandDocuments
            .AnyAsync(d => d.LandId == id && d.DocumentType.IsRequiredForVerification);
        if (!hasRequiredDocument)
            throw new BusinessRuleException("A document of a type required for verification must be uploaded before submission.");

        EnsureEligible(EvaluateEligibility(land, await GetCurrentCriterionAsync()));

        await ChangeStatusAsync(land, LandStatus.PendingVerification, RequireUserId(), "Submitted for admin verification.");
    }

    public async Task ReviseAsync(int id)
    {
        var land = await LoadLandAsync(id);
        EnsureOwner(land);

        if (land.Status != LandStatus.Rejected)
            throw new BusinessRuleException("Only rejected lands can be revised.");

        await ChangeStatusAsync(land, LandStatus.Draft, RequireUserId(), "Revised by owner and returned to Draft.");
    }

    public async Task VerifyAsync(int id)
    {
        EnsureAdmin("Only Admin or SuperAdmin can verify land.");
        var land = await LoadLandAsync(id);

        if (land.Status != LandStatus.PendingVerification)
            throw new BusinessRuleException("Land is not pending verification.");

        var criterion = await GetCurrentCriterionAsync()
            ?? throw new BusinessRuleException("No eligibility criteria configured.");

        var failed = EvaluateEligibility(land, criterion)
            .Where(r => r.Result == "Failed")
            .Select(r => r.Criterion)
            .ToList();
        if (failed.Count > 0)
            throw new BusinessRuleException("Land does not meet eligibility criteria: " + string.Join(", ", failed) + ".");

        var requiredTypeIds = await _context.DocumentTypes
            .Where(t => t.IsRequiredForVerification)
            .Select(t => t.Id)
            .ToListAsync();

        // A verification with no required document types would succeed without any
        // evidence ever being reviewed. That is a vacuous pass, so it is rejected as a
        // business-rule conflict instead of silently verifying the land.
        if (requiredTypeIds.Count == 0)
            throw new BusinessRuleException("No document types are configured as required for verification; verification cannot proceed.");

        var landDocuments = await _context.LandDocuments
            .Where(d => d.LandId == id && requiredTypeIds.Contains(d.DocumentTypeId))
            .ToListAsync();

        foreach (var typeId in requiredTypeIds)
        {
            var latest = landDocuments
                .Where(d => d.DocumentTypeId == typeId)
                .OrderByDescending(d => d.Version)
                .FirstOrDefault();

            if (latest?.Status != DocumentStatuses.Approved)
                throw new BusinessRuleException("Every required document type must have an approved latest version.");
        }

        land.VerifiedAgainstCriterionId = criterion.Id;
        land.DataVerifiedByAdmin = true;

        await ChangeStatusAsync(land, LandStatus.Verified, RequireUserId(),
            $"Verified by admin against criteria version {criterion.Id}.");
    }

    public async Task RejectAsync(int id, string reason)
    {
        EnsureAdmin("Only Admin or SuperAdmin can reject land.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationFailedException("A rejection reason is required.");

        var land = await LoadLandAsync(id);

        if (land.Status != LandStatus.PendingVerification)
            throw new BusinessRuleException("Land is not pending verification.");

        await ChangeStatusAsync(land, LandStatus.Rejected, RequireUserId(), reason);
    }

    public async Task<LandEligibilityResponse> GetEligibilityAsync(int id)
    {
        var land = await LoadLandAsync(id);
        await EnsureCanViewAsync(land);

        var criterion = await GetCurrentCriterionAsync();
        return new LandEligibilityResponse(land.Id, criterion?.Id, EvaluateEligibility(land, criterion));
    }

    public async Task<List<LandStatusHistoryDto>> GetAllStatusHistoryAsync(int landId)
    {
        var land = await LoadLandAsync(landId);
        if (!IsAdmin && land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not allowed to view this land's history.");

        var history = await _context.LandStatusHistories
            .Include(h => h.ChangedBy)
            .Where(h => h.LandId == landId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();

        return history.Select(h => new LandStatusHistoryDto
        {
            id = h.Id,
            LandId = h.LandId,
            statusName = h.Status.ToString(),
            ChangeByName = h.ChangedBy.FullName,
            Reason = h.Reason,
            ChangedAt = h.ChangedAt ?? DateTime.MinValue
        }).ToList();
    }

    public async Task<LandDocumentDto> UploadDocumentAsync(int landId, UploadLandDocumentRequest request)
    {
        var land = await LoadLandAsync(landId);
        EnsureOwner(land);

        // Evidence may only be added while the land is Draft or Rejected. Once the land
        // is PendingVerification the submitted evidence is frozen for the admin review,
        // and a Verified/UnderContract land is final. There is no re-open transition, so
        // allowing an upload in those states would silently change what is being verified.
        if (land.Status is not (LandStatus.Draft or LandStatus.Rejected))
            throw new BusinessRuleException("Documents can only be uploaded while the land is Draft or Rejected.");

        var documentType = await _context.DocumentTypes.FirstOrDefaultAsync(t => t.Id == request.DocumentTypeId)
            ?? throw new ValidationFailedException("Document type does not exist.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Serialize concurrent version allocation for this land. A transaction alone is
        // not enough: two uploads can both read the same MAX(VERSION) and then both try
        // to insert it. The update lock on the parent land row makes concurrent uploads
        // for the same land run one at a time, so the next version is always read after
        // the previous upload committed. The lock is parameterized and covers only the
        // requested land.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM LANDS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {landId}");

        var previousMax = await _context.LandDocuments
            .Where(d => d.LandId == landId && d.DocumentTypeId == documentType.Id)
            .Select(d => (int?)d.Version)
            .MaxAsync();

        var document = new LandDocument
        {
            LandId = landId,
            DocumentTypeId = documentType.Id,
            FilePath = request.FilePath,
            Version = (previousMax ?? 0) + 1,
            Status = DocumentStatuses.Pending,
            UploadedAt = DateTime.UtcNow
        };

        _context.LandDocuments.Add(document);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        document.DocumentType = documentType;
        return ToDocumentDto(document);
    }

    public async Task<List<LandDocumentDto>> GetDocumentsAsync(int landId)
    {
        var land = await LoadLandAsync(landId);
        if (!IsAdmin && land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not allowed to view this land's documents.");

        var documents = await _context.LandDocuments
            .AsNoTracking()
            .Include(d => d.DocumentType)
            .Where(d => d.LandId == landId)
            .OrderBy(d => d.DocumentTypeId)
            .ThenBy(d => d.Version)
            .ToListAsync();

        return documents.Select(ToDocumentDto).ToList();
    }

    public async Task<LandDocumentDto> ReviewDocumentAsync(int landId, int documentId, ReviewLandDocumentRequest request)
    {
        EnsureAdmin("Only Admin or SuperAdmin can review documents.");
        var land = await LoadLandAsync(landId);

        // Verified and UnderContract lands already have a frozen evidence/criterion
        // basis (VerifiedAgainstCriterionId and DataVerifiedByAdmin). There is no
        // re-review workflow, so a document review must not change that evidence.
        if (land.Status is LandStatus.Verified or LandStatus.UnderContract)
            throw new BusinessRuleException("Documents of a verified land cannot be reviewed again.");

        var document = await _context.LandDocuments
            .Include(d => d.DocumentType)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.LandId == landId)
            ?? throw new NotFoundException("Document not found.");

        if (!request.Approved && string.IsNullOrWhiteSpace(request.Note))
            throw new ValidationFailedException("A note is required when rejecting a document.");

        document.Status = request.Approved ? DocumentStatuses.Approved : DocumentStatuses.Rejected;
        document.ReviewedById = RequireUserId();
        document.ReviewedAt = DateTime.UtcNow;
        document.ReviewNote = request.Note;

        await _context.SaveChangesAsync();
        return ToDocumentDto(document);
    }

    private bool IsAdmin => _currentUser.Role is RoleNames.Admin or RoleNames.SuperAdmin;

    private void EnsureAdmin(string message)
    {
        if (!IsAdmin)
            throw new ForbiddenException(message);
    }

    private int RequireUserId() =>
        _currentUser.Id ?? throw new ForbiddenException("Authentication is required.");

    // Resource visibility convention (SEC-09): a resource that does not exist (or is
    // soft-deleted) is reported as NotFoundException -> 404. A resource that exists but
    // is not owned/viewable by the caller is reported as ForbiddenException -> 403.
    // This is applied by every load/authorize pair below; a 403 therefore reveals that
    // the resource exists, which is accepted and must not be described as concealment.
    private async Task<Land> LoadLandAsync(int id) =>
        await _context.Lands
            .Include(l => l.Region)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
        ?? throw new NotFoundException("Land not found.");

    private void EnsureOwner(Land land)
    {
        if (land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not the owner of this land.");
    }

    private async Task<bool> IsApprovedInvestorAsync(int userId) =>
        await _context.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted && u.IsApproved);

    private async Task<IQueryable<Land>> ApplyVisibilityAsync(IQueryable<Land> query)
    {
        var userId = RequireUserId();
        switch (_currentUser.Role)
        {
            case RoleNames.Admin:
            case RoleNames.SuperAdmin:
                return query;
            case RoleNames.Landlord:
                return query.Where(l => l.LandlordId == userId);
            case RoleNames.Investor:
                if (!await IsApprovedInvestorAsync(userId))
                    throw new ForbiddenException("Investor account is not approved.");
                return query.Where(l => l.Status == LandStatus.Verified);
            default:
                throw new ForbiddenException("Your role cannot browse lands.");
        }
    }

    private async Task EnsureCanViewAsync(Land land)
    {
        var userId = RequireUserId();
        switch (_currentUser.Role)
        {
            case RoleNames.Admin:
            case RoleNames.SuperAdmin:
                return;
            case RoleNames.Landlord when land.LandlordId == userId:
                return;
            case RoleNames.Investor when land.Status == LandStatus.Verified && await IsApprovedInvestorAsync(userId):
                return;
        }
        throw new ForbiddenException("You are not allowed to view this land.");
    }

    private async Task EnsureGridBelongsToRegionAsync(int gridId, int regionId)
    {
        var grid = await _context.Grids.FirstOrDefaultAsync(g => g.Id == gridId);
        if (grid == null || grid.RegionId != regionId)
            throw new ValidationFailedException("Grid must exist and belong to the selected region.");
    }

    private async Task EnsureLandNumberAvailableAsync(int regionId, string landNumber, int? excludeLandId)
    {
        var exists = await _context.Lands.AnyAsync(l =>
            l.RegionId == regionId &&
            l.LandNumber == landNumber &&
            (excludeLandId == null || l.Id != excludeLandId));

        if (exists)
            throw new BusinessRuleException(LandNumberConflictMessage);
    }

    private async Task SaveLandChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ_LAND_NUMBER_REGION"))
        {
            throw new BusinessRuleException(LandNumberConflictMessage);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ_LAND_LOCATION"))
        {
            throw new BusinessRuleException(LandLocationConflictMessage);
        }
    }

    // Only the two known business unique indexes are translated to 409. Any other
    // database failure keeps the generic error path. The index name is matched so an
    // unrelated unique constraint (e.g. a future default or primary key) is never
    // reported as a duplicate land.
    private static bool IsUniqueViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is SqlException sql &&
        (sql.Number == 2601 || sql.Number == 2627) &&
        sql.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);

    private async Task EnsureNoDuplicateParcelAsync(int regionId, string village, string basin, string parcelNumber, int? excludeLandId)
    {
        var exists = await _context.Lands.AnyAsync(l =>
            !l.IsDeleted &&
            l.RegionId == regionId &&
            l.Village == village &&
            l.Basin == basin &&
            l.ParcelNumber == parcelNumber &&
            (excludeLandId == null || l.Id != excludeLandId));

        if (exists)
            throw new BusinessRuleException(LandLocationConflictMessage);
    }

    // Criteria ordering is by Id ONLY, never UpdatedAt (approved Step-4 correction A1):
    // the seeded criterion's UPDATED_AT comes from the database default getdate() (server
    // LOCAL time) while new versions store DateTime.UtcNow (3 hours behind in Jordan), so
    // for up to ~3 hours after a fresh database build a new version can look OLDER than
    // the seed. The newest version is therefore the one with the highest Id.
    private async Task<LandCriterion?> GetCurrentCriterionAsync() =>
        await _context.LandCriteria
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

    private static IReadOnlyList<LandCriterionResult> EvaluateEligibility(Land land, LandCriterion? criterion) =>
        EvaluateEligibility(land.AreaDonum, land.SlopePercentage, land.DistanceToGridKm,
            land.SolarIrradiance, land.ElevationM, criterion);

    private static IReadOnlyList<LandCriterionResult> EvaluateEligibility(
        decimal area, decimal slope, decimal gridDistance, decimal solarIrradiance, decimal elevation,
        LandCriterion? criterion) =>
    [
        EvaluateCriterion("area", criterion?.MinAreaDonum, area, isMinimum: true),
        EvaluateCriterion("slope", criterion?.MaxSlopePct, slope, isMinimum: false),
        EvaluateCriterion("gridDistance", criterion?.MaxGridDistanceKm, gridDistance, isMinimum: false),
        EvaluateCriterion("solarIrradiance", criterion?.MinSolarIrradiance, solarIrradiance, isMinimum: true),
        EvaluateCriterion("elevation", criterion?.MinElevationM, elevation, isMinimum: true)
    ];

    private static LandCriterionResult EvaluateCriterion(string criterion, decimal? required, decimal actual, bool isMinimum)
    {
        if (required is null)
            return new LandCriterionResult(criterion, null, actual, "NotEnforced");

        var passed = isMinimum ? actual >= required.Value : actual <= required.Value;
        return new LandCriterionResult(criterion, required, actual, passed ? "Passed" : "Failed");
    }

    private static void EnsureEligible(IEnumerable<LandCriterionResult> results)
    {
        var failed = results.Where(r => r.Result == "Failed").Select(r => r.Criterion).ToList();
        if (failed.Count > 0)
            throw new ValidationFailedException("Land does not meet eligibility criteria: " + string.Join(", ", failed) + ".");
    }

    private async Task ChangeStatusAsync(Land land, LandStatus newStatus, int changedById, string? reason)
    {
        land.Status = newStatus;
        _context.LandStatusHistories.Add(NewHistory(land.Id, newStatus, changedById, reason));
        await _context.SaveChangesAsync();
    }

    private static LandStatusHistory NewHistory(int landId, LandStatus status, int changedById, string? reason) =>
        new()
        {
            LandId = landId,
            Status = status,
            ChangedById = changedById,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        };

    private static LandDto ToDto(Land land) => new()
    {
        Id = land.Id,
        LandlordId = land.LandlordId,
        RegionId = land.RegionId,
        RegionName = land.Region.NameEn,
        GridId = land.GridId,
        LandNumber = land.LandNumber,
        Village = land.Village,
        Basin = land.Basin,
        ParcelNumber = land.ParcelNumber,
        AreaDonum = land.AreaDonum,
        SlopePercentage = land.SlopePercentage,
        DistanceToGridKm = land.DistanceToGridKm,
        SolarIrradiance = land.SolarIrradiance,
        ElevationM = land.ElevationM,
        Status = land.Status.ToString(),
        DataVerifiedByAdmin = land.DataVerifiedByAdmin,
        VerifiedAgainstCriterionId = land.VerifiedAgainstCriterionId
    };

    private async Task<LandDto> ToDtoAsync(Land land)
    {
        await _context.Entry(land).Reference(l => l.Region).LoadAsync();
        return ToDto(land);
    }

    private static LandDocumentDto ToDocumentDto(LandDocument document) => new(
        document.Id,
        document.LandId,
        document.DocumentTypeId,
        document.DocumentType.Name,
        document.FilePath,
        document.Version,
        document.Status ?? "",
        document.UploadedAt,
        document.ReviewedById,
        document.ReviewedAt,
        document.ReviewNote);

    private static LandStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        if (!Enum.TryParse<LandStatus>(status, ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed) ||
            parsed.ToString() != status)
            throw new ValidationFailedException("Status must be the name of a land status.");

        return parsed;
    }
}
