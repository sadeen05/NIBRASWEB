using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Real-database concurrency checks for the requirement configuration (DOCUMENT_TYPES) and its
// interaction with the land evidence workflow. They run only in the separately gated
// --sql-acceptance mode and use the approved disposable target (SADEEN\SQLEXPRESS /
// NIBRAS_F1_Verify). Every fixture is per-run-owned (f1req-{runId}-*); no history, migration
// or unrelated row is changed or deleted. Cleanup is receipt-based and fails closed if any
// target record is not owned by the current run.
public static class LandRequirementConcurrencyAcceptance
{
    // Per-run ownership context: carries the unique run marker and exact created IDs.
    // The marker format is "f1req-{guid:N}" where the GUID is unique per RunAsync invocation.
    private sealed record RunContext(
        string Marker,
        HashSet<int> LandIds,
        HashSet<int> TypeIds,
        HashSet<int> DocumentIds,
        HashSet<int> UserIds,
        int CriterionId);

    private sealed record Fixture(
        int OwnerId, int SuperAdminId, int LandId, int TypeId, int PendingDocumentId, int CriterionId);

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    // Validates that the current criteria row is a legitimate policy row (non-null thresholds,
    // not a fixture-like row) before any test runs. Fails closed if the effective criteria
    // appears to be a test fixture.
    private static async Task<int> ValidateAndPinCurrentCriterionAsync(NebrasdbContext db)
    {
        var current = await db.LandCriteria.AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Select(c => new { c.Id, c.MinAreaDonum, c.MaxSlopePct, c.MaxGridDistanceKm, c.MinSolarIrradiance, c.Reason })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("The disposable database has no eligibility criteria to pin.");

        // Fail closed: the effective criteria must have real thresholds and a non-fixture reason.
        if (current.MinAreaDonum is null || current.MaxSlopePct is null || current.MaxGridDistanceKm is null)
            throw new InvalidOperationException($"Current criteria ID {current.Id} has null thresholds; appears to be a test fixture. Aborting to prevent contamination.");

        if (string.IsNullOrWhiteSpace(current.Reason) || current.Reason.Contains("fixture", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Current criteria ID {current.Id} has fixture-like reason '{current.Reason}'. Aborting to prevent contamination.");

        return current.Id;
    }

    // Creates a new RunContext with a unique marker for this RunAsync invocation.
    private static RunContext CreateRunContext(int criterionId)
    {
        var marker = "f1req-" + Guid.NewGuid().ToString("N");
        return new RunContext(
            Marker: marker,
            LandIds: new HashSet<int>(),
            TypeIds: new HashSet<int>(),
            DocumentIds: new HashSet<int>(),
            UserIds: new HashSet<int>(),
            CriterionId: criterionId);
    }

    // Records a created fixture ID in the run context for later ownership verification.
    private static void RecordCreation(RunContext ctx, int landId, int typeId, int documentId, int userId)
    {
        ctx.LandIds.Add(landId);
        ctx.TypeIds.Add(typeId);
        ctx.DocumentIds.Add(documentId);
        ctx.UserIds.Add(userId);
    }

    // Verifies that a given land ID belongs to the current run. Fails closed if not.
    private static void VerifyLandOwnership(RunContext ctx, int landId, string operation)
    {
        if (!ctx.LandIds.Contains(landId))
            throw new InvalidOperationException($"{operation}: land ID {landId} is not owned by the current run (marker={ctx.Marker}). Refusing to modify unowned record.");
    }

    // Verifies that a given type ID belongs to the current run. Fails closed if not.
    private static void VerifyTypeOwnership(RunContext ctx, int typeId, string operation)
    {
        if (!ctx.TypeIds.Contains(typeId))
            throw new InvalidOperationException($"{operation}: type ID {typeId} is not owned by the current run (marker={ctx.Marker}). Refusing to modify unowned record.");
    }

    // Review of an approved land's evidence is SuperAdmin-only (LandService
    // EnsureSuperAdmin), and DocumentTypeService writes are administrator operations.
    private static async Task<Exception?> WithReviewAsync(
        Fixture fixture, bool approved, string note = "f1req-review") =>
        await CaptureAsync(async () =>
        {
            await using var db = SqlAcceptance.Context();
            var service = new LandService(db, new TestUser(fixture.SuperAdminId, RoleNames.SuperAdmin), null!, new TestEnvironment());
            await service.ReviewDocumentAsync(fixture.LandId, fixture.PendingDocumentId, new ReviewLandDocumentRequest
            {
                Approved = approved,
                Note = note,
            });
        });

    private static async Task<Exception?> WithTypeUpdateAsync(int typeId, bool required, string? name = null) =>
        await CaptureAsync(async () =>
        {
            await using var db = SqlAcceptance.Context();
            var service = new DocumentTypeService(db);
            await service.UpdateAsync(typeId, new DocumentTypeRequest
            {
                Name = name ?? "f1req-type-unchanged",
                IsRequiredForVerification = required,
            });
        });

    // Creates a Verified land with a required document type and a pending/rejected v2 document.
// All created IDs are recorded in the RunContext for ownership-based cleanup.
    private static async Task<Fixture> CreateApprovedLandAsync(NebrasdbContext db, RunContext ctx, string documentTwoStatus)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var owner = await SqlAcceptance.FixtureAsync(db, true);
        var superAdmin = await SqlAcceptance.FixtureAsync(db, true, RoleNames.SuperAdmin);
        var criterionId = ctx.CriterionId;

        // Create type with run-specific marker
        var type = new DocumentType { Name = ctx.Marker + "-type-" + suffix, IsRequiredForVerification = true };
        db.DocumentTypes.Add(type);

        // Create land with run-specific marker
        var land = new Land
        {
            LandlordId = owner.Id,
            LandNumber = ctx.Marker + "-" + suffix,
            Village = "v-" + suffix,
            Basin = "b-" + suffix,
            ParcelNumber = "p-" + suffix,
            Description = "fixture",
            AreaDonum = 1000m,
            Status = LandStatus.Verified,
            DataVerifiedByAdmin = true,
            VerifiedAgainstCriterionId = criterionId,
        };
        db.Lands.Add(land);

        var now = DateTime.UtcNow;
        // v1: approved
        db.LandDocuments.Add(new LandDocument
        {
            Land = land,
            DocumentType = type,
            FilePath = "fixture/v1-" + suffix,
            Version = 1,
            Status = DocumentStatuses.Approved,
            ReviewedById = superAdmin.Id,
            ReviewedAt = now,
            UploadedAt = now,
        });
        // v2: pending or rejected
        var pending = new LandDocument
        {
            Land = land,
            DocumentType = type,
            FilePath = "fixture/v2-" + suffix,
            Version = 2,
            Status = documentTwoStatus,
            ReviewedById = documentTwoStatus == DocumentStatuses.Pending ? null : superAdmin.Id,
            ReviewedAt = documentTwoStatus == DocumentStatuses.Pending ? null : now,
            UploadedAt = now,
        };
        db.LandDocuments.Add(pending);

        await db.SaveChangesAsync();

        // Record all created IDs for ownership tracking
        RecordCreation(ctx, land.Id, type.Id, pending.Id, owner.Id);
        RecordCreation(ctx, land.Id, type.Id, 0, superAdmin.Id); // superAdmin user

        return new Fixture(owner.Id, superAdmin.Id, land.Id, type.Id, pending.Id, criterionId);
    }

    // Creates a Verified land with an optional type (for MakingRequiredHidesVerifiedLandAsync).
    // All created IDs are recorded in the RunContext.
    private static async Task<(int LandId, int TypeId, int OwnerId)> CreateVerifiedLandWithOptionalTypeAsync(NebrasdbContext db, RunContext ctx)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var owner = await SqlAcceptance.FixtureAsync(db, true);
        var criterionId = ctx.CriterionId;

        var type = new DocumentType { Name = ctx.Marker + "-type-" + suffix, IsRequiredForVerification = false };
        db.DocumentTypes.Add(type);

        var land = new Land
        {
            LandlordId = owner.Id,
            LandNumber = ctx.Marker + "-" + suffix,
            Village = "v-" + suffix,
            Basin = "b-" + suffix,
            ParcelNumber = "p-" + suffix,
            Description = "fixture",
            AreaDonum = 1000m,
            Status = LandStatus.Verified,
            DataVerifiedByAdmin = true,
            VerifiedAgainstCriterionId = criterionId,
        };
        db.Lands.Add(land);

        await db.SaveChangesAsync();

        RecordCreation(ctx, land.Id, type.Id, 0, owner.Id);

        return (land.Id, type.Id, owner.Id);
    }

    private static async Task<(bool Required, string LatestStatus, bool Hidden, int? ReviewerId)> StateOfAsync(Fixture fixture)
    {
        await using var db = SqlAcceptance.Context();
        var type = await db.DocumentTypes.AsNoTracking().SingleAsync(t => t.Id == fixture.TypeId);
        var latestStatus = (await db.LandDocuments
            .Where(d => d.LandId == fixture.LandId && d.DocumentTypeId == fixture.TypeId)
            .OrderByDescending(d => d.Version)
            .Select(d => d.Status)
            .FirstAsync())!;
        var hidden = await db.HasOutstandingRequiredDocumentAsync(fixture.LandId);
        var reviewerId = await db.LandDocuments
            .Where(d => d.Id == fixture.PendingDocumentId)
            .Select(d => d.ReviewedById)
            .SingleAsync();
        return (type.IsRequiredForVerification, latestStatus, hidden, reviewerId);
    }

    public static async Task RunAsync()
    {
        // Validate and pin the current criteria BEFORE any test runs.
        // This fails closed if the effective criteria is a fixture row.
        int criterionId;
        await using (var db = SqlAcceptance.Context())
        {
            criterionId = await ValidateAndPinCurrentCriterionAsync(db);
        }
        var ctx = CreateRunContext(criterionId);

        try
        {
            await RejectionBlocksOptionalChangeAsync(ctx);
            await OptionalChangeBlocksReReviewAsync(ctx);
            await ReReviewAndOptionalizerParallelRaceAsync(ctx);
            await MakingRequiredHidesVerifiedLandAsync(ctx);
            await UnknownTypeRefusedRollbackAsync(ctx);
        }
        finally
        {
            // Cleanup: only current-run fixtures, with exact rowcount assertions
            await RestoreAndCompleteFixturesAsync(ctx);
        }
    }

    /// <summary>
    /// Restores only the current run's document-type required flags and completes evidence
    /// only for the current run's Verified lands. Fails closed if any target is not owned
    /// by this run. Asserts exact affected row counts.
    /// </summary>
    private static async Task RestoreAndCompleteFixturesAsync(RunContext ctx)
    {
        await using var db = SqlAcceptance.Context();

        // 1. Restore only OUR required-type flags (persist BEFORE querying effective required types)
        var ours = await db.DocumentTypes
            .Where(t => ctx.TypeIds.Contains(t.Id) && t.IsRequiredForVerification)
            .ToListAsync();
        foreach (var type in ours) type.IsRequiredForVerification = false;
        if (ours.Count > 0)
        {
            var affected = await db.SaveChangesAsync();
            if (affected != ours.Count)
                throw new InvalidOperationException($"RestoreRequiredFlags: expected {ours.Count} row(s) updated, got {affected}.");
        }

        // 2. Get ONLY our Verified lands (by exact owned IDs, not prefix)
        var fixtureLandIds = ctx.LandIds.ToList();
        if (fixtureLandIds.Count == 0) return;

        // 3. Query globally required types AFTER our flags are persisted
        var requiredTypeIds = await db.DocumentTypes
            .Where(t => t.IsRequiredForVerification)
            .Select(t => t.Id)
            .ToListAsync();

        // 4. Find missing required documents ONLY for our lands
        var existing = await db.LandDocuments
            .Where(d => fixtureLandIds.Contains(d.LandId) && requiredTypeIds.Contains(d.DocumentTypeId))
            .Select(d => new { d.LandId, d.DocumentTypeId, d.Version, Status = (string?)d.Status })
            .ToListAsync();
        var approved = existing
            .GroupBy(d => (d.LandId, d.DocumentTypeId))
            .Where(g => g.OrderByDescending(d => d.Version).First().Status == DocumentStatuses.Approved)
            .Select(g => g.Key)
            .ToHashSet();

        var now = DateTime.UtcNow;
        int inserted = 0;
        foreach (var landId in fixtureLandIds)
        {
            VerifyLandOwnership(ctx, landId, "RestoreAndCompleteFixturesAsync");
            var ownerId = await db.Lands.Where(l => l.Id == landId).Select(l => l.LandlordId).SingleAsync();
            foreach (var typeId in requiredTypeIds)
            {
                // Only verify ownership for suite-owned types; global required types (e.g., TitleDeed, EncumbranceStatement)
                // are legitimately completed on our lands without owning the type definition.
                if (ctx.TypeIds.Contains(typeId))
                    VerifyTypeOwnership(ctx, typeId, "RestoreAndCompleteFixturesAsync");
                if (approved.Contains((landId, typeId)))
                    continue;
                var nextVersion = existing
                    .Where(d => d.LandId == landId && d.DocumentTypeId == typeId)
                    .Select(d => d.Version)
                    .DefaultIfEmpty(0)
                    .Max() + 1;
                var document = new LandDocument
                {
                    LandId = landId,
                    DocumentTypeId = typeId,
                    FilePath = "fixture/" + ctx.Marker + "-completion-" + Guid.NewGuid().ToString("N"),
                    Version = nextVersion,
                    Status = DocumentStatuses.Approved,
                    ReviewedById = ownerId,
                    ReviewedAt = now,
                    UploadedAt = now,
                };
                db.LandDocuments.Add(document);
                existing.Add(new { LandId = landId, DocumentTypeId = typeId, Version = nextVersion, Status = (string?)DocumentStatuses.Approved });
                approved.Add((landId, typeId));
                inserted++;
            }
        }

        if (inserted > 0)
        {
            var affected = await db.SaveChangesAsync();
            if (affected != inserted)
                throw new InvalidOperationException($"RestoreAndCompleteFixturesAsync: expected {inserted} document row(s) inserted, got {affected}.");
        }
    }

    // Deterministic first interleaving: the re-review renders its decision first, so the
    // required→optional decision must be refused instead of silently cancelling the
    // reviewer's rejection out of the derived evidence.
    private static async Task RejectionBlocksOptionalChangeAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var fixture = await CreateApprovedLandAsync(setup, ctx, DocumentStatuses.Rejected);

        var update = await WithTypeUpdateAsync(fixture.TypeId, required: false);
        var state = await StateOfAsync(fixture);

        Program.Check(update is BusinessRuleException,
            "making a required type optional after its re-review rejection is a controlled conflict");
        Program.Check(state.Required && state.LatestStatus == DocumentStatuses.Rejected && state.Hidden,
            "refused optional change keeps the type required and the land hidden");
        var name = await QueryTypeNameAsync(fixture.TypeId);
        Program.Check(name.StartsWith(ctx.Marker + "-type-"),
            "refused optional change discards the whole request, not the flag only");
    }

    // Deterministic second interleaving: the optional change commits first, so the re-review
    // precondition (outstanding required evidence) is gone at decision time and the review is
    // refused instead of writing a rejection nothing would gate on.
    private static async Task OptionalChangeBlocksReReviewAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var fixture = await CreateApprovedLandAsync(setup, ctx, DocumentStatuses.Pending);

        var update = await WithTypeUpdateAsync(fixture.TypeId, required: false);
        var review = await WithReviewAsync(fixture, approved: false);
        var (required, latest, _, reviewerId) = await StateOfAsync(fixture);

        Program.Check(update is null && review is BusinessRuleException,
            "re-review after an optional change is a controlled conflict, not a silent rejection");
        Program.Check(!required && latest == DocumentStatuses.Pending && reviewerId is null,
            "refused re-review leaves the pending version unreviewed and unchanged");
    }

    // The actual race: both operations start while the land's latest version is an unreviewed
    // correction and the type is required. Whichever wins the DOCUMENT_TYPES row lock commits
    // first and the other must end closed; the unsafe state (type optional AND a freshly
    // rejected latest version) must be unreachable either way.
    private static async Task ReReviewAndOptionalizerParallelRaceAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var fixture = await CreateApprovedLandAsync(setup, ctx, DocumentStatuses.Pending);

        var results = await Task.WhenAll(
            WithReviewAsync(fixture, approved: false),
            WithTypeUpdateAsync(fixture.TypeId, required: false));
        var (required, latest, _, reviewerId) = await StateOfAsync(fixture);

        Program.Check(results.Count(r => r is null) == 1 && results.Count(r => r is BusinessRuleException) == 1,
            "parallel re-review/optional change has one winner and one controlled conflict");
        Program.Check(!(required == false && latest == DocumentStatuses.Rejected),
            "parallel race never leaves the type optional with a freshly rejected latest version");

        // Proof for both winner orders, without assuming the lock arbitration label:
        if (reviewerId is not null)
        {
            Program.Check(required && latest == DocumentStatuses.Rejected && results[0] is null,
                "review-first order commits the rejection and the update refuses after it");
        }
        else
        {
            Program.Check(!required && latest == DocumentStatuses.Pending && results[1] is null,
                "update-first order commits the optional change and the re-review refuses after it");
        }
    }

    // Fail-safe complement: making a type required never invalidates a stored verification
    // snapshot (no status/pin rewrite) - it only derives the land hidden again while the newly
    // required version is missing, which is the documented behaviour.
    private static async Task MakingRequiredHidesVerifiedLandAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var (landId, typeId, ownerId) = await CreateVerifiedLandWithOptionalTypeAsync(setup, ctx);

        var outstandingBefore = await setup.OutstandingRequiredDocumentTypeIdsAsync(landId);
        var update = await WithTypeUpdateAsync(typeId, required: true);
        var outstandingAfter = await setup.OutstandingRequiredDocumentTypeIdsAsync(landId);

        await using var verify = SqlAcceptance.Context();
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == landId);

        // The shared DB may carry other required types (which also keep this fixture hidden),
        // so the assertion is scoped to this suite's own type: once it is required, it joins
        // the land's outstanding evidence and the land stays hidden until its evidence exists.
        Program.Check(update is null && !outstandingBefore.Contains(typeId) && outstandingAfter.Contains(typeId),
            "making a type required re-derives the verified land as hidden without rewriting it");
        Program.Check(final.Status == LandStatus.Verified && final.VerifiedAgainstCriterionId == ctx.CriterionId &&
            final.DataVerifiedByAdmin,
            "the requiredness change preserves the verification snapshot exactly");
    }

    private static async Task<string> QueryTypeNameAsync(int typeId)
    {
        await using var db = SqlAcceptance.Context();
        return await db.DocumentTypes.AsNoTracking()
            .Where(t => t.Id == typeId)
            .Select(t => t.Name)
            .SingleAsync();
    }

    // Exit paths of the update under the new transaction scope must roll back completely:
    // an unknown identifier is a controlled 404 and leaves no open transaction behind.
    private static async Task UnknownTypeRefusedRollbackAsync(RunContext ctx)
    {
        var refused = await WithTypeUpdateAsync(int.MaxValue, required: false);
        Program.Check(refused is NotFoundException,
            "update of an unknown document type is a controlled not-found, not an unexpected error");
        // Restore our own flags on exit (the finally block in RunAsync will also do this,
        // but this ensures cleanup even if an unexpected exception propagates from here).
        await RestoreAndCompleteFixturesAsync(ctx);
    }
}
