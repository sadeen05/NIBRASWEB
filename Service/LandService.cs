using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

    // L1: a stored snapshot whose JSON cannot be read is never replaced by an empty, current or
    // guessed one. An approval fails closed with these controlled conflicts; a rejection never
    // deserializes a snapshot at all (see DecideChangeRequestAsync), so a request with unreadable
    // stored JSON can always be decided and can never trap its land. The stored row is left
    // untouched either way, and the read endpoints report the snapshot as unavailable instead of
    // substituting one.
    private const string OldSnapshotUnreadableMessage =
        "The stored before-change snapshot of this request cannot be read, so the change cannot be validated. Rejecting the request is still possible; the stored data is left untouched.";
    private const string NewSnapshotUnreadableMessage =
        "The stored proposed-change snapshot of this request cannot be read, so the change cannot be applied. Rejecting the request is still possible; the stored data is left untouched.";

    // L2: an approved land is decided against the criteria version it was verified against, never
    // against the latest one. If that version is not recorded or no longer exists the check cannot
    // be performed, so the positive decision fails closed with these controlled conflicts. The
    // referenced version is the only correct repair; a rejection stays available at every call
    // site, and the reviewing authority keeps the operation.
    private const string PinnedCriteriaUnavailableMessage =
        "The criteria version this land was approved against is unavailable, so the change cannot be validated against it. Rejecting the request is still possible; the stored criteria basis must be restored before a change can be approved.";
    private const string PinnedCriteriaReviewUnavailableMessage =
        "The criteria version this land was approved against is unavailable, so its required documents cannot be approved. The land stays hidden from investors until the stored criteria basis is restored.";

    /// <summary>
    /// Prefix of the dynamic "the land no longer meets the criteria version it was approved
    /// against" message. The frontend localizes the prefix and names the failing criteria
    /// (see <c>criteriaFormat.ts</c>), so the prefix is a public contract.
    /// </summary>
    public const string PinnedCriteriaFailurePrefix =
        "The land no longer meets the criteria version it was approved against: ";
    private readonly NebrasdbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentStorage _documentStorage;
    private readonly IHostEnvironment _environment;

    public LandService(
        NebrasdbContext context,
        ICurrentUser currentUser,
        IDocumentStorage documentStorage,
        IHostEnvironment environment)
    {
        _context = context;
        _currentUser = currentUser;
        _documentStorage = documentStorage;
        _environment = environment;
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
            .Include(l => l.Governorate)
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

        var ids=lands.Select(l => l.Id).ToList();
        var registrations=await _context.LandStatusHistories.AsNoTracking()
            .Where(h => ids.Contains(h.LandId) && h.Status==LandStatus.Draft && h.Reason=="Land created as Draft.")
            .GroupBy(h => h.LandId).Select(g => new {LandId=g.Key,At=g.OrderBy(h => h.Id).Select(h => h.ChangedAt).FirstOrDefault()})
            .ToDictionaryAsync(h => h.LandId,h => h.At);
        var items=lands.Select(ToDto).ToList();
        foreach(var item in items) item.RegisteredAt=registrations.GetValueOrDefault(item.Id);
        return new LandListResponse(items, pageNumber, pageSize, totalCount, totalPages);
    }

    public async Task<LandDto> GetByIdAsync(int id)
    {
        var land = await LoadLandAsync(id);
        await EnsureCanViewAsync(land);
        return await ToDtoAsync(land);
    }

    public async Task<LandDto> CreateAsync(CreateLandRequest request)
    {
        if (_currentUser.Role != RoleNames.Landlord)
            throw new ForbiddenException("Only landlords can register land.");

        var userId = RequireUserId();

        await EnsureGovernorateExistsAsync(request.GovernorateId);
        await EnsureNoDuplicateParcelAsync(request.GovernorateId, null, request.Village, request.Basin, request.ParcelNumber, null);
        await EnsureLandNumberAvailableAsync(request.GovernorateId, null, request.LandNumber, null);

        // Region, grid and the four technical measurements are admin-only and are recorded
        // later through the assessment endpoint. They stay NULL here and are never replaced
        // by a zero default, so an enforced technical criterion is reported as NotAssessed
        // (never as a pass) until an assessment supplies the measurement.
        var results = EvaluateEligibility(
            request.AreaDonum, null, null, null, null, await GetCurrentCriterionAsync());
        EnsureEligible(results);

        var land = new Land
        {
            LandlordId = userId,
            GovernorateId = request.GovernorateId,
            LandNumber = request.LandNumber,
            Village = request.Village,
            Basin = request.Basin,
            ParcelNumber = request.ParcelNumber,
            Description = request.Description,
            AreaDonum = request.AreaDonum,
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
        // The land row is pinned before any state this edit acts on is read. Without it an owner
        // edit and a submit/revise/reject or a document review/upload for the same land can
        // interleave: the edit can commit after a submission reviewed the old data, and a
        // concurrent review can approve evidence the edit is about to invalidate. The lock is the
        // same LANDS UPDLOCK/HOLDLOCK the other land-scoped mutations take (land first), so they
        // run one at a time and the existing lock order is preserved.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await LockLandAsync(id);
        EnsureOwner(locked);

        if (locked.Status is not (LandStatus.Draft or LandStatus.Rejected))
            throw new BusinessRuleException("Only lands in Draft or Rejected status can be edited.");

        // The no-tracking lock copy above guards the state. The write goes through a tracked
        // entity loaded under the same lock, exactly as the other land mutations do.
        var land = await _context.Lands
            .Include(l => l.Contracts)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        if (land.Contracts.Any(c => c.Status == ContractStatus.Active))
            throw new BusinessRuleException("Cannot edit a land that has an active contract.");

        // The governorate is optional on update so a land registered before governorates
        // existed can still be edited without retroactively assigning one. When supplied
        // it is validated and the duplicate keys move with it.
        if (request.GovernorateId is { } requestedGovernorateId)
            await EnsureGovernorateExistsAsync(requestedGovernorateId);

        var effectiveGovernorateId = request.GovernorateId ?? land.GovernorateId;

        await EnsureNoDuplicateParcelAsync(
            effectiveGovernorateId, land.RegionId, request.Village, request.Basin, request.ParcelNumber, id);
        await EnsureLandNumberAvailableAsync(effectiveGovernorateId, land.RegionId, request.LandNumber, id);

        // Region, grid and the technical measurements are admin-only: this request never
        // carries them, so an owner edit can neither overwrite nor erase an assessment.
        var results = EvaluateEligibility(
            request.AreaDonum, land.SlopePercentage, land.DistanceToGridKm,
            land.SolarIrradiance, land.ElevationM, await GetCurrentCriterionAsync());
        EnsureEligible(results);

        // A document review is a judgement about the data that was shown to the reviewer.
        // When the owner changes the land's identity, location or area, the previous
        // approvals no longer describe the current data, so they stop counting and must be
        // repeated. A description-only edit does not invalidate anything: the description
        // is not part of the reviewed evidence, and invalidating on it would only create
        // pointless re-review work.
        var reviewedDataChanged =
            land.LandNumber != request.LandNumber ||
            land.Village != request.Village ||
            land.Basin != request.Basin ||
            land.ParcelNumber != request.ParcelNumber ||
            land.AreaDonum != request.AreaDonum ||
            (request.GovernorateId is { } changedGovernorateId && land.GovernorateId != changedGovernorateId);

        if (request.GovernorateId is { } newGovernorateId)
            land.GovernorateId = newGovernorateId;
        land.LandNumber = request.LandNumber;
        land.Village = request.Village;
        land.Basin = request.Basin;
        land.ParcelNumber = request.ParcelNumber;
        land.Description = request.Description;
        land.AreaDonum = request.AreaDonum;

        if (reviewedDataChanged)
            await InvalidateApprovedDocumentsAsync(id);

        await SaveLandChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SubmitAsync(int id)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await LockLandAsync(id);
        EnsureOwner(locked);

        if (locked.Status != LandStatus.Draft)
            throw new BusinessRuleException("Only lands in Draft status can be submitted.");

        // Tracked row for the write, read under the lock. The lock serializes this submission
        // against a second submit/revise/reject and against a document upload or review, so the
        // status check, the evidence check and the history append all see one consistent state.
        var land = await _context.Lands
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        if (string.IsNullOrWhiteSpace(land.Village) ||
            string.IsNullOrWhiteSpace(land.Basin) ||
            string.IsNullOrWhiteSpace(land.ParcelNumber))
            throw new BusinessRuleException("Village, basin and parcel number are required before submission.");

        var hasRequiredDocument = await _context.LandDocuments
            .AnyAsync(d => d.LandId == id && d.DocumentType.IsRequiredForVerification);
        if (!hasRequiredDocument)
            throw new BusinessRuleException("A document of a type required for verification must be uploaded before submission.");

        EnsureEligible(EvaluateEligibility(land, await GetCurrentCriterionAsync()));

        // A land that was already rejected is a correction, not a first submission: its
        // resubmission goes to the SuperAdmin queue, never back into the regular Admin
        // queue. A first submission keeps the existing PendingVerification route.
        var wasRejected = await _context.LandStatusHistories
            .AnyAsync(h => h.LandId == id && h.Status == LandStatus.Rejected);

        var (targetStatus, reason) = wasRejected
            ? (LandStatus.PendingSuperAdminReview, "Resubmitted after rejection for SuperAdmin review.")
            : (LandStatus.PendingVerification, "Submitted for admin verification.");

        await ChangeStatusAsync(land, targetStatus, RequireUserId(), reason);
        await transaction.CommitAsync();
    }

    public async Task ReviseAsync(int id)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await LockLandAsync(id);
        EnsureOwner(locked);

        if (locked.Status != LandStatus.Rejected)
            throw new BusinessRuleException("Only rejected lands can be revised.");

        var land = await _context.Lands
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        await ChangeStatusAsync(land, LandStatus.Draft, RequireUserId(), "Revised by owner and returned to Draft.");
        await transaction.CommitAsync();
    }

    public async Task VerifyAsync(int id, string? note = null)
    {
        EnsureAdmin("Only Admin or SuperAdmin can verify land.");

        // L3: verification decides on the land's required evidence and then writes the land's
        // status, and a document review or a correction upload of the same land changes exactly
        // that evidence. The land row is therefore pinned with the same UPDLOCK/HOLDLOCK those
        // paths take before any state this decision rests on is read, and everything read
        // (status, criteria, required types, documents) and written (status, pinned criterion,
        // history) happens inside this one transaction: a review cannot approve the last
        // outstanding document after the evidence check read it as outstanding, nor reject an
        // approved one after the check read it as approved. Lock order follows the existing
        // convention - LANDS first, its documents after - so it cannot invert against the
        // upload/review/change-request paths (they take the same lock first).
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var land = await LockLandAsync(id);

        EnsureReviewAuthority(land);

        if (land.Status is not (LandStatus.PendingVerification or LandStatus.PendingSuperAdminReview))
            throw new BusinessRuleException("Land is not pending verification.");

        var criterion = await GetCurrentCriterionAsync()
            ?? throw new BusinessRuleException("No eligibility criteria configured.");

        var results = EvaluateEligibility(land, criterion);

        var failed = results
            .Where(r => r.Result == "Failed")
            .Select(r => r.Criterion)
            .ToList();
        if (failed.Count > 0)
            throw new BusinessRuleException("Land does not meet eligibility criteria: " + string.Join(", ", failed) + ".");

        // A missing measurement is never a pass: final verification requires every
        // enforced criterion to have been assessed.
        var notAssessed = results
            .Where(r => r.Result == "NotAssessed")
            .Select(r => r.Criterion)
            .ToList();
        if (notAssessed.Count > 0)
            throw new BusinessRuleException("Technical assessment is incomplete: " + string.Join(", ", notAssessed) + ".");

        // A verified land must carry an assigned grid that belongs to its assigned region.
        if (land.GridId is null || land.RegionId is null)
            throw new BusinessRuleException(
                "The land has no assigned region and grid; technical assessment is required before verification.");

        var assignedGrid = await _context.Grids.AsNoTracking().FirstOrDefaultAsync(g => g.Id == land.GridId);
        if (assignedGrid is null || assignedGrid.RegionId != land.RegionId)
            throw new BusinessRuleException("The assigned grid does not belong to the assigned region.");

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

        // The write goes through a tracking read of the same row, which this transaction already
        // pins (LockLandAsync returns a no-tracking copy). The status re-read below happens under
        // that lock, so it cannot observe a state the evidence checks above did not see.
        var verified = await LoadLandAsync(id);
        verified.VerifiedAgainstCriterionId = criterion.Id;
        verified.DataVerifiedByAdmin = true;

        // The approval note shares the append-only history reason with the criterion
        // version the decision was taken against, so supplying a note never loses the
        // version that was verified against. A null/blank note leaves the recorded text
        // byte-for-byte identical to the previous behaviour.
        var reason = $"Verified by admin against criteria version {criterion.Id}.";
        if (!string.IsNullOrWhiteSpace(note))
            reason += $" Note: {note}";

        await ChangeStatusAsync(verified, LandStatus.Verified, RequireUserId(), reason);
        await transaction.CommitAsync();
    }

    public async Task RejectAsync(int id, string reason)
    {
        EnsureAdmin("Only Admin or SuperAdmin can reject land.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationFailedException("A rejection reason is required.");

        // The same land lock VerifyAsync takes. A reject and a verify (or a second reject) for
        // the same pending land therefore serialize: exactly one decision commits, and a stale
        // caller gets the controlled conflict instead of appending a contradictory history row.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await LockLandAsync(id);

        EnsureReviewAuthority(locked);

        if (locked.Status is not (LandStatus.PendingVerification or LandStatus.PendingSuperAdminReview))
            throw new BusinessRuleException("Land is not pending verification.");

        var land = await _context.Lands
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        await ChangeStatusAsync(land, LandStatus.Rejected, RequireUserId(), reason);
        await transaction.CommitAsync();
    }

    public async Task<LandEligibilityResponse> GetEligibilityAsync(int id)
    {
        var land = await LoadLandAsync(id);
        await EnsureCanViewAsync(land);

        // Approved land eligibility must describe its pinned evidence, not today's thresholds.
        var criterion = land.VerifiedAgainstCriterionId is int pinnedId
            ? await _context.LandCriteria.AsNoTracking().SingleOrDefaultAsync(c => c.Id == pinnedId)
                ?? throw new BusinessRuleException(PinnedCriteriaReviewUnavailableMessage)
            : await GetCurrentCriterionAsync();
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
            ChangedById = h.ChangedById,
            Reason = h.Reason,
            ChangedAt = h.ChangedAt ?? DateTime.MinValue
        }).ToList();
    }

    /// <summary>
    /// Legacy metadata-only registration. It is kept only for the historical Development
    /// regression suites; a deployed environment must use the multipart upload so that the
    /// stored row always points at a real, validated file.
    /// </summary>
    public Task<LandDocumentDto> UploadDocumentAsync(int landId, UploadLandDocumentRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            throw new ValidationFailedException(
                "Metadata-only document registration is not available. Upload the document file.");
        }

        return UploadDocumentCoreAsync(
            landId,
            request.DocumentTypeId,
            file: null,
            legacyFilePath: request.FilePath,
            allowMetadataOnly: true);
    }

    public Task<LandDocumentDto> UploadDocumentAsync(int landId, int documentTypeId, IFormFile file) =>
        UploadDocumentCoreAsync(
            landId,
            documentTypeId,
            file,
            legacyFilePath: null,
            allowMetadataOnly: false);

    private async Task<LandDocumentDto> UploadDocumentCoreAsync(
        int landId,
        int documentTypeId,
        IFormFile? file,
        string? legacyFilePath,
        bool allowMetadataOnly)
    {
        // Existence and ownership are checked first so a missing or foreign land is rejected
        // (404/403) without contending on the row lock and without writing a file for it.
        var existing = await LoadLandAsync(landId);
        EnsureOwner(existing);

        if (file is null && (!allowMetadataOnly || string.IsNullOrWhiteSpace(legacyFilePath)))
        {
            throw new ValidationFailedException("The document file is required.");
        }

        // The file is validated and written before the database transaction starts, so a large
        // upload never holds the land lock while the bytes are copied. The finally block removes
        // it again if the row cannot be committed.
        StoredDocument? stored = null;
        if (file is not null)
        {
            stored = await _documentStorage.SaveAsync(file, landId);
        }

        var committed = false;
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Pin the land row before reading anything the guard below depends on. The entity
            // read above predates the transaction and may already be stale: a concurrent
            // document review (which takes the same lock) can have approved the last outstanding
            // document, and approving a document is exactly what makes the evidence complete
            // again. Reading the authoritative row and evidence under the lock is what stops
            // this guard from passing on stale state and then inserting a version that
            // supersedes valid evidence. The lock is the same one the version allocation below
            // needs, and it covers only this land.
            var land = await LockLandAsync(landId);
            EnsureOwner(land);

            // Evidence may only be added while the land is Draft or Rejected. Once the land is
            // PendingVerification the submitted evidence is frozen for the admin review, and a
            // Verified/UnderContract land is final: allowing an upload in those states would
            // silently change what is being verified.
            //
            // The one exception is the owner's correction of an approved land whose required
            // evidence is outstanding: a required document type whose latest version is not
            // Approved because a SuperAdmin rejected it during the re-review, or because an
            // approved change request invalidated its approval. Only a new version of one of
            // those types may be added. It is added as Pending and never overwrites the rejected
            // version, and on an approved land only a SuperAdmin can review it, so the owner can
            // correct evidence but can never self-approve, supersede valid evidence, or bypass
            // the SuperAdmin review.
            if (land.Status is not (LandStatus.Draft or LandStatus.Rejected))
            {
                var correctableTypeIds = land.Status is LandStatus.Verified or LandStatus.UnderContract
                    ? await _context.OutstandingRequiredDocumentTypeIdsAsync(landId)
                    : new List<int>();

                if (correctableTypeIds.Count == 0)
                    throw new BusinessRuleException("Documents can only be uploaded while the land is Draft or Rejected.");

                if (!correctableTypeIds.Contains(documentTypeId))
                    throw new BusinessRuleException(
                        "On an approved land only the document types whose latest version is not approved can be corrected.");
            }

            var documentType = await _context.DocumentTypes.FirstOrDefaultAsync(t => t.Id == documentTypeId)
                ?? throw new ValidationFailedException("Document type does not exist.");

            // The lock taken above also serializes concurrent version allocation for this land.
            // A transaction alone is not enough: two uploads can both read the same MAX(VERSION)
            // and then both try to insert it, so the next version is always read after the
            // previous upload committed.
            var previousMax = await _context.LandDocuments
                .Where(d => d.LandId == landId && d.DocumentTypeId == documentType.Id)
                .Select(d => (int?)d.Version)
                .MaxAsync();

            var document = new LandDocument
            {
                LandId = landId,
                DocumentTypeId = documentType.Id,
                FilePath = stored?.RelativePath ?? legacyFilePath!,
                OriginalFileName = stored?.OriginalFileName,
                ContentType = stored?.ContentType,
                FileSizeBytes = stored?.SizeBytes,
                Sha256 = stored?.Sha256,
                Version = (previousMax ?? 0) + 1,
                Status = DocumentStatuses.Pending,
                UploadedAt = DateTime.UtcNow
            };

            _context.LandDocuments.Add(document);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (DatabaseErrors.IsForeignKeyViolation(ex))
            {
                // The document type was deleted between the lookup and the insert. The
                // transaction is rolled back by the scope, and the finally block removes the
                // file that was already written.
                throw new ValidationFailedException("The selected document type no longer exists.");
            }

            await transaction.CommitAsync();
            committed = true;

            document.DocumentType = documentType;
            return ToDocumentDto(document);
        }
        finally
        {
            if (stored is not null && !committed)
            {
                await _documentStorage.DeleteAsync(stored.RelativePath);
            }
        }
    }

    public async Task<LandDocumentDownload> DownloadDocumentAsync(int landId, int documentId)
    {
        var land = await LoadLandAsync(landId);
        if (!IsAdmin && land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not allowed to view this land's documents.");

        var document = await _context.LandDocuments
            .AsNoTracking()
            .Include(d => d.DocumentType)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.LandId == landId)
            ?? throw new NotFoundException("Document not found.");

        if (document.FileSizeBytes is null ||
            string.IsNullOrWhiteSpace(document.OriginalFileName) ||
            string.IsNullOrWhiteSpace(document.FilePath))
        {
            throw new NotFoundException("The document file is not available.");
        }

        var stream = _documentStorage.OpenRead(document.FilePath);
        return new LandDocumentDownload(
            stream,
            document.OriginalFileName,
            document.ContentType ?? "application/octet-stream",
            document.FileSizeBytes.Value);
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

        // A review and a correction upload of the same land must not interleave: the upload
        // decides whether it may add a version from the land's evidence, and the review is what
        // can change that evidence. Both pin the land row with the same UPDLOCK inside their
        // transaction, so the status, the evidence and the document version this review acts on
        // are read after the other operation has committed or not started at all.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var land = await LockLandAsync(landId);

        // The documents of a resubmission are reviewed by the same authority that
        // decides the resubmission (SuperAdmin), so a regular Admin cannot act on a
        // land that was sent to the SuperAdmin queue.
        EnsureReviewAuthority(land);

        var document = await _context.LandDocuments
            .Include(d => d.DocumentType)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.LandId == landId)
            ?? throw new NotFoundException("Document not found.");

        // The document type row is pinned with the same UPDLOCK, HOLDLOCK the required→optional
        // decision in DocumentTypeService.UpdateAsync takes on its row. This path holds the LANDS
        // lock already, so the order (LANDS first, then its types) matches the existing convention
        // and cannot invert against that update. The tracking read refreshes the Include-loaded
        // navigation in place, so the requiredness this review acts on below is the value under
        // the lock: an optional change and a re-review of this type serialize, and the guard on an
        // approved land (evidence outstanding) is never evaluated on a stale flag.
        await _context.DocumentTypes
            .FromSqlInterpolated($"SELECT * FROM DOCUMENT_TYPES WITH (UPDLOCK, HOLDLOCK) WHERE ID = {document.DocumentTypeId}")
            .FirstAsync();

        // Verified and UnderContract lands already have a frozen evidence/criterion basis
        // (VerifiedAgainstCriterionId and DataVerifiedByAdmin). The single exception is a land
        // whose required evidence is currently outstanding: an approved change request
        // invalidated an approval, or a previous re-review rejected a required document. Only
        // then, and only for the latest version of a document type, is a document reviewable
        // again, so the verification keeps resting on evidence that describes the land's
        // current data and is approved. Every other document on an approved land stays frozen
        // (409), exactly as before.
        //
        // The frozen case is decided before the authority check on purpose: a document that
        // cannot be reviewed at all is a conflict for every caller, so a regular Admin still
        // receives 409 (and not 403) for it.
        if (land.Status is LandStatus.Verified or LandStatus.UnderContract)
        {
            var isLatestVersion = !await _context.LandDocuments.AnyAsync(d =>
                d.LandId == landId && d.DocumentTypeId == document.DocumentTypeId && d.Version > document.Version);

            // The re-review is of an outstanding required type itself, not merely of a land
            // whose evidence is outstanding for some type. The required→optional decision
            // (DocumentTypeService.UpdateAsync) serializes against this review on the
            // DOCUMENT_TYPES row lock, so if it made this type optional first, the type is no
            // longer in the outstanding set and this review cannot write a decision the
            // derived evidence would ignore - either interleaving stays fail-closed.
            var outstandingTypeIds = await _context.OutstandingRequiredDocumentTypeIdsAsync(landId);
            if (!isLatestVersion || !outstandingTypeIds.Contains(document.DocumentTypeId))
                throw new BusinessRuleException("Documents of a verified land cannot be reviewed again.");

            // The re-review belongs to the SuperAdmin, who owns the approved-land change
            // workflow; a regular Admin never acts on an approved land's evidence.
            EnsureSuperAdmin("Only a SuperAdmin can re-review the documents of an approved land.");
        }

        if (!request.Approved && string.IsNullOrWhiteSpace(request.Note))
            throw new ValidationFailedException("A note is required when rejecting a document.");

        // Approving a required document is what can make the evidence complete again, and is
        // therefore what can restore the land's investor visibility. At that moment the land
        // must still satisfy the criteria version it was verified against, so visibility is
        // never restored onto a land that fails its pinned criteria. The data is frozen while a
        // land is approved and a change request is validated against the same pinned version,
        // so this is expected to hold; it exists so the restore is never a blind one.
        if (request.Approved &&
            document.DocumentType.IsRequiredForVerification &&
            land.Status is LandStatus.Verified or LandStatus.UnderContract)
            await EnsurePinnedCriteriaStillSatisfiedAsync(land);

        document.Status = request.Approved ? DocumentStatuses.Approved : DocumentStatuses.Rejected;
        document.ReviewedById = RequireUserId();
        document.ReviewedAt = DateTime.UtcNow;
        document.ReviewNote = request.Note;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDocumentDto(document);
    }

    /// <summary>
    /// SuperAdmin-only technical assessment. Region/grid and the four technical
    /// measurements are written here and nowhere else; the request can never change
    /// status, owner, deletion flag, verification flags, criterion version or
    /// governorate. The write is partial (an omitted value keeps its stored value) and
    /// appends one append-only snapshot of the effective values, so the history can be
    /// reconstructed. Regular Admin is denied: land data may only be changed by a
    /// SuperAdmin (or the owning landlord through the owner edit endpoint).
    /// </summary>
    public async Task<LandDto> AssessAsync(int landId, AssessLandRequest request)
    {
        EnsureSuperAdmin("Only a SuperAdmin can change the technical assessment.");

        // An assessment writes the technical measurements and - when a supplied value actually
        // differs from the stored one - withdraws this land's document approvals, i.e. the same
        // evidence a verification or a document review decides on. It therefore takes the same
        // land lock those paths take before it reads any state it acts on, and re-reads the
        // status under it: an assessment cannot land on a land that was verified while this
        // request was being prepared, and its document writes cannot invert against a
        // verification holding the land lock (LANDS is always locked before its documents).
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await LockLandAsync(landId);

        // Verified and UnderContract lands already have a frozen evidence/criterion basis
        // (VerifiedAgainstCriterionId / DataVerifiedByAdmin). There is no re-assessment
        // workflow, so an assessment must not change that evidence.
        if (locked.Status is LandStatus.Verified or LandStatus.UnderContract)
            throw new BusinessRuleException("A verified land cannot be reassessed.");

        var land = await _context.Lands
            .Include(l => l.Region)
            .Include(l => l.Governorate)
            .FirstOrDefaultAsync(l => l.Id == landId && !l.IsDeleted)
            ?? throw new NotFoundException("Land not found.");

        var hasRegion = request.RegionId is not null;
        var hasGrid = request.GridId is not null;
        if (hasRegion != hasGrid)
            throw new ValidationFailedException("Region and grid must be provided together.");

        // An assessment can change the very measurements a document review was based on.
        // When a supplied value actually differs from the stored one, the existing document
        // approvals stop counting and the evidence must be reviewed again (verified lands
        // are already rejected above, so this only affects pre-verification states).
        var assessmentChanged =
            (request.RegionId is { } assessedRegionId && land.RegionId != assessedRegionId) ||
            (request.GridId is { } assessedGridId && land.GridId != assessedGridId) ||
            (request.SlopePercentage is { } assessedSlope && land.SlopePercentage != assessedSlope) ||
            (request.DistanceToGridKm is { } assessedDistance && land.DistanceToGridKm != assessedDistance) ||
            (request.SolarIrradiance is { } assessedSolar && land.SolarIrradiance != assessedSolar) ||
            (request.ElevationM is { } assessedElevation && land.ElevationM != assessedElevation);

        if (request.RegionId is { } regionId)
        {
            if (!await _context.Regions.AnyAsync(r => r.Id == regionId))
                throw new ValidationFailedException("Region does not exist.");

            await EnsureGridBelongsToRegionAsync(request.GridId!.Value, regionId);
            land.RegionId = regionId;
            land.GridId = request.GridId;
            // Setting the FK directly does not refresh the navigation property, and the reference
            // was already loaded by the Include above (so LoadAsync would be a no-op). Assign the
            // region explicitly, otherwise the response would carry the previously loaded name.
            land.Region = await _context.Regions.FirstOrDefaultAsync(r => r.Id == regionId);
        }

        // A null means "leave unchanged"; Phase 1 has no explicit clearing.
        if (request.SlopePercentage is { } slope)
            land.SlopePercentage = slope;
        if (request.DistanceToGridKm is { } gridDistance)
            land.DistanceToGridKm = gridDistance;
        if (request.SolarIrradiance is { } solarIrradiance)
            land.SolarIrradiance = solarIrradiance;
        if (request.ElevationM is { } elevation)
            land.ElevationM = elevation;

        // Append-only snapshot of the effective values after this assessment commits.
        // Actual nulls are preserved; nothing is defaulted to zero.
        _context.LandAssessments.Add(new LandAssessment
        {
            LandId = land.Id,
            AssessedById = RequireUserId(),
            AreaDonumSnapshot = land.AreaDonum,
            CriterionId = land.VerifiedAgainstCriterionId ?? await _context.LandCriteria.OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(),
            AssessedAt = DateTime.UtcNow,
            Note = request.Note,
            RegionId = land.RegionId,
            GridId = land.GridId,
            SlopePercentage = land.SlopePercentage,
            DistanceToGridKm = land.DistanceToGridKm,
            SolarIrradiance = land.SolarIrradiance,
            ElevationM = land.ElevationM
        });

        if (assessmentChanged)
            await InvalidateApprovedDocumentsAsync(land.Id);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(land);
    }

    /// <summary>
    /// Assessment history, readable by the land owner and by Admin/SuperAdmin only.
    /// Rows are ordered oldest-first so the sequence reconstructs the changes.
    /// </summary>
    public async Task<List<LandAssessmentDto>> GetAssessmentsAsync(int landId)
    {
        var land = await LoadLandAsync(landId);
        if (!IsAdmin && land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not allowed to view this land's assessment history.");

        var assessments = await _context.LandAssessments
            .AsNoTracking()
            .Include(a => a.AssessedBy)
            .Include(a => a.Criterion)
            .Where(a => a.LandId == landId)
            .OrderBy(a => a.AssessedAt)
            .ThenBy(a => a.Id)
            .ToListAsync();

        return assessments.Select(a => new LandAssessmentDto(
            a.Id,
            a.LandId,
            a.AssessedById,
            a.AssessedBy.FullName,
            a.AssessedAt,
            a.Note,
            a.RegionId,
            a.GridId,
            a.SlopePercentage,
            a.DistanceToGridKm,
            a.SolarIrradiance,
            a.ElevationM, a.CriterionId, a.AreaDonumSnapshot,
            a.Criterion == null ? null : new CriterionValuesDto(a.Criterion.Id,
                a.Criterion.MinAreaDonum, a.Criterion.MaxSlopePct, a.Criterion.MaxGridDistanceKm,
                a.Criterion.MinSolarIrradiance, a.Criterion.MinElevationM))).ToList();
    }

    /// <summary>
    /// Landlord change request for an already-approved land. The land is hidden from
    /// investors immediately and stays hidden until a SuperAdmin decides the request.
    /// The original field values are stored as an immutable snapshot before anything
    /// else, so the pre-change state can always be recovered.
    /// </summary>
    public async Task<LandChangeRequestDto> SubmitChangeRequestAsync(CreateLandChangeRequestRequest request)
    {
        var land = await LoadLandAsync(request.LandId);
        EnsureOwner(land);

        if (land.Status != LandStatus.Verified)
            throw new BusinessRuleException("A change request can only be made for an approved land.");

        var hasPending = await _context.LandChangeRequests
            .AnyAsync(c => c.LandId == land.Id && c.Status == ChangeRequestStatuses.Pending);
        if (hasPending)
            throw new BusinessRuleException("This land already has a change request awaiting review.");

        var supportingDocuments = request.SupportingDocuments
            .Select(d => new ChangeRequestDocumentDto { DocumentTypeId = d.DocumentTypeId, FilePath = d.FilePath })
            .ToList();

        if (supportingDocuments.Count > 0)
        {
            var typeIds = supportingDocuments.Select(d => d.DocumentTypeId).Distinct().ToList();
            var knownTypeIds = await _context.DocumentTypes
                .Where(t => typeIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync();
            if (typeIds.Except(knownTypeIds).Any())
                throw new ValidationFailedException("A supporting document type does not exist.");
        }

        var changeRequest = new LandChangeRequest
        {
            LandId = land.Id,
            RequestedById = RequireUserId(),
            Reason = request.Reason,
            Status = ChangeRequestStatuses.Pending,
            RequestedAt = DateTime.UtcNow,
            OldSnapshot = JsonSerializer.Serialize(SnapshotOf(land)),
            NewSnapshot = JsonSerializer.Serialize(SnapshotOf(request)),
            SupportingDocuments = supportingDocuments.Count == 0
                ? null
                : JsonSerializer.Serialize(supportingDocuments)
        };

        _context.LandChangeRequests.Add(changeRequest);

        // Hidden for the whole review. The status stays Verified so the existing
        // offer/contract transitions are untouched, but the land is removed from every
        // investor list and detail response until the decision is recorded.
        land.IsHiddenFromInvestors = true;
        _context.LandStatusHistories.Add(NewHistory(
            land.Id, LandStatus.Verified, RequireUserId(),
            Truncate($"Change request submitted; land hidden from investors until review. {request.Reason}", 500)));

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UX_LAND_CHANGE_REQUESTS_PENDING_LAND"))
        {
            // The filtered unique index is the database-level guarantee behind the pre-check
            // above: when a concurrent submission for the same land loses the race it is
            // reported as the same 409 instead of surfacing as a 500.
            throw new BusinessRuleException("This land already has a change request awaiting review.");
        }

        return await GetChangeRequestAsync(changeRequest.Id);
    }

    /// <summary>Change requests of one land, readable by its owner and by Admin/SuperAdmin.</summary>
    public async Task<List<LandChangeRequestDto>> GetChangeRequestsAsync(int landId)
    {
        var land = await LoadLandAsync(landId);
        if (!IsAdmin && land.LandlordId != RequireUserId())
            throw new ForbiddenException("You are not allowed to view this land's change requests.");

        var rows = await _context.LandChangeRequests
            .AsNoTracking()
            .Include(c => c.Land)
            .Include(c => c.RequestedBy)
            .Include(c => c.ReviewedBy)
            .Where(c => c.LandId == landId)
            .OrderByDescending(c => c.Id)
            .ToListAsync();

        return rows.Select(MapChangeRequest).ToList();
    }

    /// <summary>The SuperAdmin review queue: every change request still awaiting a decision.</summary>
    public async Task<List<LandChangeRequestDto>> GetPendingChangeRequestsAsync()
    {
        EnsureSuperAdmin("Only a SuperAdmin can review change requests.");

        var rows = await _context.LandChangeRequests
            .AsNoTracking()
            .Include(c => c.Land)
            .Include(c => c.RequestedBy)
            .Include(c => c.ReviewedBy)
            .Where(c => c.Status == ChangeRequestStatuses.Pending)
            .OrderBy(c => c.Id)
            .ToListAsync();

        return rows.Select(MapChangeRequest).ToList();
    }

    /// <summary>
    /// SuperAdmin decision on a change request. Approval applies the proposed fields to
    /// the land (validated against the criteria version the land was approved against)
    /// and keeps that criteria version; rejection leaves the original data untouched.
    /// Either way the reviewer identity, the decision, the note and the timestamp are
    /// recorded and the hidden-for-review flag is cleared. Investor visibility is then derived
    /// from the evidence: the land is shown again only once every required document type has an
    /// approved latest version (see <see cref="LandEvidence"/>), so an approval that invalidated
    /// the document approvals keeps the land hidden until the SuperAdmin re-reviews them.
    /// </summary>
    public async Task<LandChangeRequestDto> DecideChangeRequestAsync(
        int changeRequestId, ReviewLandChangeRequestRequest request)
    {
        EnsureSuperAdmin("Only a SuperAdmin can approve or reject a change request.");

        var changeRequest = await _context.LandChangeRequests
            .Include(c => c.Land)
            .FirstOrDefaultAsync(c => c.Id == changeRequestId)
            ?? throw new NotFoundException("Change request not found.");

        if (changeRequest.Status != ChangeRequestStatuses.Pending)
            throw new BusinessRuleException("This change request has already been decided.");

        var land = changeRequest.Land;

        // Pin the land row before any state of this decision is read or written, exactly as the
        // correction upload and the document review do (and as the offer/contract transitions do).
        // This decision writes LANDS, LAND_CHANGE_REQUESTS and - when an approving change moves the
        // land's reviewed data - LAND_DOCUMENTS in one batch. Without an explicit land lock the
        // order of those statements is EF's choice, while a concurrent document review/upload
        // holds the land UPDLOCK and then takes a LAND_DOCUMENTS lock, so an inverted batch order
        // could deadlock (each transaction holding what the other waits for). Taking the same lock
        // first fixes the order explicitly - LANDS before LAND_DOCUMENTS and LAND_CHANGE_REQUESTS.
        // It also serializes the decision against a concurrent re-review of this land, so the
        // invalidation below cannot interleave with a review and the review's pinned-criteria
        // re-check reads the land's post-decision values.
        //
        // The tracked entity read above stays the source of the field values: the only writer of an
        // approved land's fields is this same decision flow, and it is limited to one pending
        // request per land (UX_LAND_CHANGE_REQUESTS_PENDING_LAND) plus the request ROW_VERSION,
        // while the hidden-for-review flag blocks the offer path that could otherwise move the land
        // to UnderContract concurrently.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockLandAsync(land.Id);

        if (request.Approved)
        {
            // The change can only be applied while the land is still the approved land
            // the request was made against. A rejection is always allowed so a request
            // can never be left stuck.
            if (land.Status != LandStatus.Verified)
                throw new BusinessRuleException("The land is no longer approved; the change cannot be applied.");

            // L1: the snapshots are read only on this path, where they are actually needed, and
            // are never substituted when the stored JSON cannot be read. An approval fails closed
            // with a controlled conflict; a rejection never deserializes them at all, so it stays
            // possible and the request can never be stuck. The stored row is not repaired,
            // rewritten or replaced.
            var oldSnapshot = TrySnapshotFromJson(changeRequest.OldSnapshot)
                ?? throw new BusinessRuleException(OldSnapshotUnreadableMessage);

            // A verified land's fields are only writable through this flow, so a mismatch here
            // means the land moved on after this request captured its snapshot. Refuse instead
            // of overwriting the newer data with a stale proposal.
            if (!SnapshotMatches(oldSnapshot, SnapshotOf(land)))
                throw new BusinessRuleException(
                    "The land changed after this change request was submitted, so its stored proposal is stale and cannot be applied.");

            var proposed = TrySnapshotFromJson(changeRequest.NewSnapshot)
                ?? throw new BusinessRuleException(NewSnapshotUnreadableMessage);

            if (proposed.GovernorateId is { } governorateId)
                await EnsureGovernorateExistsAsync(governorateId);

            var effectiveGovernorateId = proposed.GovernorateId ?? land.GovernorateId;
            await EnsureNoDuplicateParcelAsync(
                effectiveGovernorateId, land.RegionId,
                proposed.Village, proposed.Basin, proposed.ParcelNumber, land.Id);
            await EnsureLandNumberAvailableAsync(
                effectiveGovernorateId, land.RegionId, proposed.LandNumber, land.Id);
            await EnsureFrozenAreaCriterionAsync(land, proposed.AreaDonum);

            if (proposed.GovernorateId is { } newGovernorateId)
                land.GovernorateId = newGovernorateId;
            land.LandNumber = proposed.LandNumber;
            land.Village = proposed.Village;
            land.Basin = proposed.Basin;
            land.ParcelNumber = proposed.ParcelNumber;
            land.AreaDonum = proposed.AreaDonum;
            land.Description = proposed.Description;

            // VerifiedAgainstCriterionId / DataVerifiedByAdmin are deliberately preserved:
            // the approval stays tied to the criteria version it was granted against, and a
            // later criteria change never silently rewrites a previous approval.

            // The document approvals were a judgement about the *old* data. When the approved
            // change moves the land's identity, location or area, only the approvals that were
            // a judgement about that data stop counting and must be reviewed again; a
            // description-only change keeps every approval. The comparison is against the land's
            // effective post-approval values, not the raw proposal, so a proposal that leaves a
            // field unchanged (e.g. a null governorate that keeps the stored one) never
            // re-opens an approval. Nothing is deleted and the previous review stays on the row,
            // so the audit trail survives.
            if (ReviewedDataChanged(oldSnapshot, SnapshotOf(land)))
                await InvalidateApprovedDocumentsAsync(land.Id);

            changeRequest.Status = ChangeRequestStatuses.Approved;
        }
        else
        {
            // The original data is untouched; only the decision is recorded.
            changeRequest.Status = ChangeRequestStatuses.Rejected;
        }

        // Rejection restores the flag because the original approved state is unchanged; whether
        // the land is actually shown again is derived from the evidence (see LandEvidence), so a
        // land whose required documents are still outstanding stays hidden.
        land.IsHiddenFromInvestors = false;
        changeRequest.ReviewedById = RequireUserId();
        changeRequest.ReviewedAt = DateTime.UtcNow;
        changeRequest.DecisionReason = request.DecisionReason;

        var decision = request.Approved ? "approved" : "rejected";
        _context.LandStatusHistories.Add(NewHistory(
            land.Id, land.Status, RequireUserId(),
            Truncate($"Change request #{changeRequest.Id} {decision}. {request.DecisionReason}", 500)));

        try
        {
            await SaveLandChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rowversion predicate matched no row because a concurrent decision updated the
            // request first. Nothing was written (the land change and the history row roll back
            // with it), the first decision stands, and this caller is told to reload instead of
            // silently overwriting the winner's decision.
            throw new BusinessRuleException(
                "This change request was already decided by another reviewer. Reload it and try again.");
        }

        await transaction.CommitAsync();

        return await GetChangeRequestAsync(changeRequest.Id);
    }

    private async Task<LandChangeRequestDto> GetChangeRequestAsync(int id)
    {
        var changeRequest = await _context.LandChangeRequests
            .AsNoTracking()
            .Include(c => c.Land)
            .Include(c => c.RequestedBy)
            .Include(c => c.ReviewedBy)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException("Change request not found.");

        return MapChangeRequest(changeRequest);
    }

    private static LandFieldsSnapshot SnapshotOf(Land land) => new()
    {
        GovernorateId = land.GovernorateId,
        LandNumber = land.LandNumber,
        Village = land.Village ?? "",
        Basin = land.Basin ?? "",
        ParcelNumber = land.ParcelNumber ?? "",
        AreaDonum = land.AreaDonum,
        Description = land.Description
    };

    private static LandFieldsSnapshot SnapshotOf(CreateLandChangeRequestRequest request) => new()
    {
        GovernorateId = request.GovernorateId,
        LandNumber = request.LandNumber,
        Village = request.Village,
        Basin = request.Basin,
        ParcelNumber = request.ParcelNumber,
        AreaDonum = request.AreaDonum,
        Description = request.Description
    };

    // L1: a stored snapshot is never replaced by an empty, current or guessed one. A row whose
    // JSON cannot be read - or parses but carries no usable proposal, such as "{}" - yields null
    // here, so an approval fails closed with a controlled conflict (DecideChangeRequestAsync) and
    // a read reports the snapshot as unavailable instead of raising an uncontrolled 500. A
    // rejection never needs a snapshot at all, so a request with unusable stored JSON can always
    // be decided and never traps its land.
    private static LandFieldsSnapshot? TrySnapshotFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<LandFieldsSnapshot>(json);

            // L1: syntactically valid JSON can still fail to describe a proposal at all (for
            // example "{}", which deserializes to default values). Applying it would blank the
            // land's identity fields and its area, and a read would present those blanks as the
            // stored data, so it is treated exactly like JSON that cannot be read. The create
            // request enforces the same requirements, so a snapshot rejected here can never be
            // one the API itself stored.
            if (snapshot is null ||
                string.IsNullOrWhiteSpace(snapshot.LandNumber) ||
                string.IsNullOrWhiteSpace(snapshot.Village) ||
                string.IsNullOrWhiteSpace(snapshot.Basin) ||
                string.IsNullOrWhiteSpace(snapshot.ParcelNumber) ||
                snapshot.AreaDonum < 0.0001m ||
                snapshot.AreaDonum > 99999999.99m)
            {
                return null;
            }

            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Same rule for the optional supporting-document references: unreadable JSON is reported as
    // unavailable (null), never as an empty or repaired list. A missing/blank column is the
    // stored "no supporting documents" case and stays an empty list.
    private static List<ChangeRequestDocumentDto>? TrySupportingDocumentsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ChangeRequestDocumentDto>();

        try
        {
            return JsonSerializer.Deserialize<List<ChangeRequestDocumentDto>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Full field equality (including the description) used to prove that a pending request
    // still describes the land it was made against before its proposal is applied.
    private static bool SnapshotMatches(LandFieldsSnapshot a, LandFieldsSnapshot b) =>
        a.GovernorateId == b.GovernorateId &&
        a.LandNumber == b.LandNumber &&
        a.Village == b.Village &&
        a.Basin == b.Basin &&
        a.ParcelNumber == b.ParcelNumber &&
        a.AreaDonum == b.AreaDonum &&
        a.Description == b.Description;

    // The landlord-owned fields a document review is a judgement about. A description is
    // narrative, not reviewed evidence, so it is excluded, exactly as in the owner edit:
    // a description-only change never re-opens a document approval.
    private static bool ReviewedDataChanged(LandFieldsSnapshot before, LandFieldsSnapshot after) =>
        before.GovernorateId != after.GovernorateId ||
        before.LandNumber != after.LandNumber ||
        before.Village != after.Village ||
        before.Basin != after.Basin ||
        before.ParcelNumber != after.ParcelNumber ||
        before.AreaDonum != after.AreaDonum;

    private static LandChangeRequestDto MapChangeRequest(LandChangeRequest changeRequest) => new(
        changeRequest.Id,
        changeRequest.LandId,
        changeRequest.Land?.LandNumber ?? "",
        changeRequest.Status,
        changeRequest.Reason,
        changeRequest.RequestedAt,
        changeRequest.RequestedById,
        changeRequest.RequestedBy?.FullName ?? "",
        TrySnapshotFromJson(changeRequest.OldSnapshot),
        TrySnapshotFromJson(changeRequest.NewSnapshot),
        TrySupportingDocumentsFromJson(changeRequest.SupportingDocuments),
        changeRequest.ReviewedById,
        changeRequest.ReviewedBy?.FullName,
        changeRequest.ReviewedAt,
        changeRequest.DecisionReason);

    // A change request is evaluated against the criteria version the land was approved
    // against, never against whatever the current criteria happen to be, so a criteria
    // update cannot silently invalidate an existing approval.
    private async Task EnsureFrozenAreaCriterionAsync(Land land, decimal areaDonum)
    {
        // L2: fail closed. An approved land is expected to carry the criteria version it was
        // verified against, and that version is expected to still exist (criteria rows are
        // append-only), so a missing pin or a missing row is inconsistent historical data and
        // the change is refused with a controlled conflict. Substituting the latest version
        // would silently move the approval to criteria it was never granted against. Only the
        // approval path reaches this guard, so a rejection stays possible and the request can
        // never be stuck; the referenced version is the documented repair, and restoring it is
        // an authorized data operation (there is no in-app criteria delete).
        var criterionId = land.VerifiedAgainstCriterionId
            ?? throw new BusinessRuleException(PinnedCriteriaUnavailableMessage);

        var criterion = await _context.LandCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criterionId)
            ?? throw new BusinessRuleException(PinnedCriteriaUnavailableMessage);

        if (criterion.MinAreaDonum is { } minArea && areaDonum < minArea)
            throw new BusinessRuleException(
                $"The proposed area is below the minimum ({minArea}) of the criteria version this land was approved against.");
    }

    /// <summary>
    /// Re-asserts that an approved land still satisfies the criteria version it was verified
    /// against before its evidence is allowed to become fully valid again. A land that fails
    /// this cannot be presented to investors as fully valid, so the check runs at the exact
    /// point where a re-approval would otherwise restore its visibility. It is a guard, not a
    /// new workflow: the land's owner-editable data is frozen while it is approved and a change
    /// request is validated against the same pinned version, so it is expected to hold and a
    /// failure points at an inconsistent row.
    /// <para>
    /// The guard fails closed (L2): a missing pin or a missing criteria row means the check
    /// cannot be performed, so it throws instead of substituting the current criteria, because
    /// the referenced version is the only basis on which a re-approval may restore visibility.
    /// Rejection of a document is unaffected, and restoring the referenced row is the
    /// documented repair.
    /// </para>
    /// </summary>
    private async Task EnsurePinnedCriteriaStillSatisfiedAsync(Land land)
    {
        // L2: fail closed. This guard is what lets a re-approval restore investor visibility,
        // so it may only pass against the criteria version the land was actually verified
        // against. A missing pin or a missing row means the check cannot be performed at all:
        // the approval is refused with a controlled conflict instead of substituting the
        // current criteria. Rejection remains available, and the documented recovery is an
        // authorized repair that restores the referenced LAND_CRITERIA row; nothing here
        // rewrites the pin or the stored approval.
        var criterionId = land.VerifiedAgainstCriterionId
            ?? throw new BusinessRuleException(PinnedCriteriaReviewUnavailableMessage);

        var criterion = await _context.LandCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criterionId)
            ?? throw new BusinessRuleException(PinnedCriteriaReviewUnavailableMessage);

        var blocked = EvaluateEligibility(land, criterion)
            .Where(r => r.Result is "Failed" or "NotAssessed")
            .Select(r => r.Criterion)
            .ToList();
        if (blocked.Count > 0)
            throw new BusinessRuleException(
                PinnedCriteriaFailurePrefix + string.Join(", ", blocked) + ".");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private bool IsAdmin => _currentUser.Role is RoleNames.Admin or RoleNames.SuperAdmin;

    private void EnsureAdmin(string message)
    {
        if (!IsAdmin)
            throw new ForbiddenException(message);
    }

    // Land data (region, grid and the technical measurements) is writable only by a
    // SuperAdmin. A regular Admin keeps the review/verify/reject path, which does not
    // edit land data; this service check mirrors the controller's SuperAdmin-only
    // [Authorize] so the rule also holds if an attribute is ever removed.
    private bool IsSuperAdmin => _currentUser.Role == RoleNames.SuperAdmin;

    private void EnsureSuperAdmin(string message)
    {
        if (!IsSuperAdmin)
            throw new ForbiddenException(message);
    }

    // A first submission (PendingVerification) is reviewed by Admin or SuperAdmin. A
    // resubmission after a rejection (PendingSuperAdminReview) is reviewed by a
    // SuperAdmin only, so a regular Admin can never act on the SuperAdmin queue even
    // if a controller attribute is ever removed.
    private void EnsureReviewAuthority(Land land)
    {
        if (land.Status == LandStatus.PendingSuperAdminReview)
            EnsureSuperAdmin("Only a SuperAdmin can review a land that was resubmitted after rejection.");
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
            .Include(l => l.Governorate)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted)
        ?? throw new NotFoundException("Land not found.");

    // Land-scoped evidence mutations (the correction upload and the document review) pin the LAND
    // row with UPDLOCK, HOLDLOCK inside their transaction, exactly as OfferService and
    // ContractService pin the land/contract rows for the offer and contract transitions. Every
    // such mutation takes this lock before it reads any state it later acts on, so two mutations
    // for the same land run one at a time and the status, the evidence and the document version a
    // guard decides on cannot be changed by the other operation between the check and the write.
    // The returned entity is a no-tracking copy of the row's committed-within-this-transaction
    // values; the caller may already track the same land (via LoadLandAsync), so tracking here
    // would collide with it. The lock is held until the caller commits or rolls back.
    private async Task<Land> LockLandAsync(int landId) =>
        await _context.Lands
            .FromSqlInterpolated($"SELECT * FROM LANDS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {landId} AND IS_DELETED = 0")
            .AsNoTracking()
            .FirstOrDefaultAsync()
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
                // A verified land is shown to investors only while its evidence is complete
                // (the latest version of every required document type is Approved) and no
                // change request is open. The evidence check is derived from the documents,
                // never stored: a required document rejected during the SuperAdmin re-review,
                // or an approval invalidated by an approved change request, keeps the land
                // hidden until a corrected/latest version is Approved again.
                var outstandingEvidence = await _context
                    .VerifiedLandsWithOutstandingRequiredDocumentAsync();
                return query.Where(l => l.Status == LandStatus.Verified
                    && !l.IsHiddenFromInvestors
                    && !outstandingEvidence.Contains(l.Id));
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
            // The same evidence rule as the investor list applies to a single land: a
            // verified land whose required evidence is outstanding stays invisible even
            // when it is not hidden by an open change request.
            case RoleNames.Investor when land.Status == LandStatus.Verified &&
                                         !land.IsHiddenFromInvestors &&
                                         await IsApprovedInvestorAsync(userId) &&
                                         !await _context.HasOutstandingRequiredDocumentAsync(land.Id):
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

    private async Task EnsureGovernorateExistsAsync(int governorateId)
    {
        if (!await _context.Governorates.AnyAsync(g => g.Id == governorateId))
            throw new ValidationFailedException("Governorate does not exist.");
    }

    private async Task EnsureLandNumberAvailableAsync(
        int? governorateId, int? regionId, string landNumber, int? excludeLandId)
    {
        var exists = await _context.Lands.AnyAsync(l =>
            l.LandNumber == landNumber &&
            (governorateId != null
                ? l.GovernorateId == governorateId
                : l.GovernorateId == null && l.RegionId == regionId) &&
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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ_LAND_NUMBER_REGION") ||
                                           IsUniqueViolation(ex, "UQ_LAND_NUMBER_GOV"))
        {
            throw new BusinessRuleException(LandNumberConflictMessage);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UQ_LAND_LOCATION") ||
                                           IsUniqueViolation(ex, "UQ_LAND_LOCATION_GOV"))
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

    // Duplicate detection follows the same key as the database indexes: a land that
    // carries a governorate is keyed on (governorate, village, basin, parcel); a legacy
    // record with no governorate keeps the original region-scoped rule.
    private async Task EnsureNoDuplicateParcelAsync(
        int? governorateId, int? regionId, string village, string basin, string parcelNumber, int? excludeLandId)
    {
        var exists = await _context.Lands.AnyAsync(l =>
            !l.IsDeleted &&
            l.Village == village &&
            l.Basin == basin &&
            l.ParcelNumber == parcelNumber &&
            (governorateId != null
                ? l.GovernorateId == governorateId
                : l.GovernorateId == null && l.RegionId == regionId) &&
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
        decimal area, decimal? slope, decimal? gridDistance, decimal? solarIrradiance, decimal? elevation,
        LandCriterion? criterion) =>
    [
        EvaluateCriterion("area", criterion?.MinAreaDonum, area, isMinimum: true),
        EvaluateCriterion("slope", criterion?.MaxSlopePct, slope, isMinimum: false),
        EvaluateCriterion("gridDistance", criterion?.MaxGridDistanceKm, gridDistance, isMinimum: false),
        EvaluateCriterion("solarIrradiance", criterion?.MinSolarIrradiance, solarIrradiance, isMinimum: true),
        EvaluateCriterion("elevation", criterion?.MinElevationM, elevation, isMinimum: true)
    ];

    private static LandCriterionResult EvaluateCriterion(
        string criterion, decimal? required, decimal? actual, bool isMinimum)
    {
        if (required is null)
            return new LandCriterionResult(criterion, null, actual, "NotEnforced");

        // An enforced criterion with no recorded measurement is NOT a pass: it is
        // reported as NotAssessed and blocks final verification. The missing value is
        // preserved as null and is never substituted with a zero default.
        if (actual is null)
            return new LandCriterionResult(criterion, required, null, "NotAssessed");

        var passed = isMinimum ? actual.Value >= required.Value : actual.Value <= required.Value;
        return new LandCriterionResult(criterion, required, actual, passed ? "Passed" : "Failed");
    }

    private static void EnsureEligible(IEnumerable<LandCriterionResult> results)
    {
        var failed = results.Where(r => r.Result == "Failed").Select(r => r.Criterion).ToList();
        if (failed.Count > 0)
            throw new ValidationFailedException("Land does not meet eligibility criteria: " + string.Join(", ", failed) + ".");
    }

    /// <summary>
    /// Withdraws the "Approved" state of a land's documents because the basis of that
    /// review changed (the owner corrected the land data, or an admin changed the technical
    /// assessment). Documents return to "Pending" so they must be reviewed again;
    /// <see cref="VerifyAsync"/> only accepts the latest version of every required type as
    /// Approved, so a changed land can no longer be verified on stale approvals.
    /// <para>
    /// The previous review is deliberately preserved on the row (<c>ReviewedById</c>,
    /// <c>ReviewedAt</c>, <c>ReviewNote</c>) and the version is untouched: a document whose
    /// status is Pending while <c>ReviewedAt</c> is set has been reviewed before and
    /// invalidated since, which is what the UI reports as "re-review required". Nothing is
    /// deleted and no approval is converted into a rejection, so the audit trail survives.
    /// </para>
    /// <para>The caller owns the unit of work; this method only stages the change.</para>
    /// </summary>
    private async Task InvalidateApprovedDocumentsAsync(int landId)
    {
        var approved = await _context.LandDocuments
            .Where(d => d.LandId == landId && d.Status == DocumentStatuses.Approved)
            .ToListAsync();

        foreach (var document in approved)
            document.Status = DocumentStatuses.Pending;
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
        RegionName = land.Region?.NameEn,
        GridId = land.GridId,
        GovernorateId = land.GovernorateId,
        GovernorateName = land.Governorate?.NameEn,
        LandNumber = land.LandNumber,
        Village = land.Village,
        Basin = land.Basin,
        ParcelNumber = land.ParcelNumber,
        Description = land.Description,
        AreaDonum = land.AreaDonum,
        SlopePercentage = land.SlopePercentage,
        DistanceToGridKm = land.DistanceToGridKm,
        SolarIrradiance = land.SolarIrradiance,
        ElevationM = land.ElevationM,
        Status = land.Status.ToString(),
        DataVerifiedByAdmin = land.DataVerifiedByAdmin,
        VerifiedAgainstCriterionId = land.VerifiedAgainstCriterionId,
        IsHiddenFromInvestors = land.IsHiddenFromInvestors
    };

    private async Task<LandDto> ToDtoAsync(Land land)
    {
        await _context.Entry(land).Reference(l => l.Region).LoadAsync();
        await _context.Entry(land).Reference(l => l.Governorate).LoadAsync();
        var result=ToDto(land);
        result.RegisteredAt=await _context.LandStatusHistories.AsNoTracking()
            .Where(h => h.LandId==land.Id && h.Status==LandStatus.Draft && h.Reason=="Land created as Draft.")
            .OrderBy(h => h.Id).Select(h => h.ChangedAt).FirstOrDefaultAsync();
        return result;
    }

    private static LandDocumentDto ToDocumentDto(LandDocument document) => new(
        document.Id,
        document.LandId,
        document.DocumentTypeId,
        document.DocumentType.Name,
        document.FileSizeBytes is null ? document.FilePath : null,
        document.Version,
        document.Status ?? "",
        document.UploadedAt,
        document.ReviewedById,
        document.ReviewedAt,
        document.ReviewNote,
        document.OriginalFileName,
        document.ContentType,
        document.FileSizeBytes,
        document.Sha256,
        document.FileSizeBytes is not null && !string.IsNullOrWhiteSpace(document.OriginalFileName));

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
