using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Real-database concurrency checks for the land state machine. They run only in the
// separately gated --sql-acceptance mode and use the approved disposable target
// (SADEEN\SQLEXPRESS / NIBRAS_F1_Verify). Every fixture is per-run-owned (f1race-{runId}-*);
// no history, migration or unrelated row is changed or deleted. Cleanup is receipt-based
// and fails closed if any target record is not owned by the current run.
public static class LandConcurrencyAcceptance
{
    // Per-run ownership context: carries the unique run marker and exact created IDs.
    // The marker format is "f1race-{guid:N}" where the GUID is unique per RunAsync invocation.
    private sealed record RunContext(
        string Marker,
        HashSet<int> LandIds,
        HashSet<int> TypeIds,
        HashSet<int> DocumentIds,
        HashSet<int> UserIds);

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static async Task<Exception?> WithServiceAsync(
        int userId, string role, Func<LandService, Task> action)
    {
        await using var db = SqlAcceptance.Context();
        var service = new LandService(db, new TestUser(userId, role), null!, new TestEnvironment());
        return await CaptureAsync(() => action(service));
    }

    // Creates a new RunContext with a unique marker for this RunAsync invocation.
    private static RunContext CreateRunContext()
    {
        var marker = "f1race-" + Guid.NewGuid().ToString("N");
        return new RunContext(
            Marker: marker,
            LandIds: new HashSet<int>(),
            TypeIds: new HashSet<int>(),
            DocumentIds: new HashSet<int>(),
            UserIds: new HashSet<int>());
    }

    // Records a created fixture ID in the run context for later ownership verification.
    private static void RecordCreation(RunContext ctx, int landId, int typeId, int documentId, int userId)
    {
        ctx.LandIds.Add(landId);
        ctx.TypeIds.Add(typeId);
        if (documentId > 0) ctx.DocumentIds.Add(documentId);
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

    private static async Task<Land> CreateLandAsync(
        NebrasdbContext db, RunContext ctx, int ownerId, LandStatus status, bool withRequiredDocument = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var land = new Land
        {
            LandlordId = ownerId,
            LandNumber = ctx.Marker + "-" + suffix,
            Village = "v-" + suffix,
            Basin = "b-" + suffix,
            ParcelNumber = "p-" + suffix,
            Description = "before",
            AreaDonum = 1000m,
            Status = status,
        };
        db.Lands.Add(land);

        int typeId = 0;
        int documentId = 0;

        if (withRequiredDocument)
        {
            var type = new DocumentType { Name = ctx.Marker + "-type-" + suffix, IsRequiredForVerification = true };
            db.DocumentTypes.Add(type);
            typeId = type.Id;

            var document = new LandDocument
            {
                Land = land,
                DocumentType = type,
                FilePath = "fixture/" + suffix,
                Version = 1,
                Status = DocumentStatuses.Pending,
                UploadedAt = DateTime.UtcNow,
            };
            db.LandDocuments.Add(document);
            documentId = document.Id;
        }

        await db.SaveChangesAsync();

        RecordCreation(ctx, land.Id, typeId, documentId, ownerId);

        return land;
    }

    private static UpdateLandRequest EditOf(Land land, string? description = null) => new()
    {
        GovernorateId = land.GovernorateId,
        LandNumber = land.LandNumber,
        Village = land.Village ?? "",
        Basin = land.Basin ?? "",
        ParcelNumber = land.ParcelNumber ?? "",
        AreaDonum = land.AreaDonum,
        Description = description ?? land.Description,
    };

    public static async Task RunAsync()
    {
        var ctx = CreateRunContext();

        // Restore only our own required-type flags before and after
        await RestoreRequiredFlagsAsync(ctx);
        try
        {
            await ParallelSubmitAsync(ctx);
            await ParallelReviseAsync(ctx);
            await ParallelRejectAsync(ctx);
            await StaleUpdateAfterSubmitAsync(ctx);
            await SequentialUpdateThenSubmitAsync(ctx);
            await SubmitAndUpdateRaceAsync(ctx);
            await FailedVerificationLeavesNoPartialStateAsync(ctx);
        }
        finally
        {
            await RestoreRequiredFlagsAsync(ctx);
        }
    }

    /// <summary>
    /// Restores only the current run's document-type required flags.
    /// Fails closed if any target is not owned by this run. Asserts exact affected row counts.
    /// </summary>
    private static async Task RestoreRequiredFlagsAsync(RunContext ctx)
    {
        await using var db = SqlAcceptance.Context();

        var ours = await db.DocumentTypes
            .Where(t => ctx.TypeIds.Contains(t.Id) && t.IsRequiredForVerification)
            .ToListAsync();
        if (ours.Count == 0) return;

        foreach (var type in ours) type.IsRequiredForVerification = false;
        var affected = await db.SaveChangesAsync();
        if (affected != ours.Count)
            throw new InvalidOperationException($"RestoreRequiredFlagsAsync: expected {ours.Count} row(s) updated, got {affected}.");
    }

    private static async Task ParallelSubmitAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.Draft, withRequiredDocument: true);

        var results = await Task.WhenAll(
            WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.SubmitAsync(land.Id)),
            WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.SubmitAsync(land.Id)));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "ParallelSubmitAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var pending = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.PendingVerification);

        Program.Check(results.Count(r => r is null) == 1 && results.Count(r => r is BusinessRuleException) == 1,
            "parallel submit has exactly one winner and one controlled conflict");
        Program.Check(final.Status == LandStatus.PendingVerification && pending == 1,
            "parallel submit writes exactly one PendingVerification history row");
    }

    private static async Task ParallelReviseAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.Rejected);

        var results = await Task.WhenAll(
            WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.ReviseAsync(land.Id)),
            WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.ReviseAsync(land.Id)));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "ParallelReviseAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var drafts = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.Draft);

        Program.Check(results.Count(r => r is null) == 1 && results.Count(r => r is BusinessRuleException) == 1,
            "parallel revise has exactly one winner and one controlled conflict");
        Program.Check(final.Status == LandStatus.Draft && drafts == 1,
            "parallel revise writes exactly one Draft history row");
    }

    private static async Task ParallelRejectAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        var admin = await SqlAcceptance.FixtureAsync(setup, true, RoleNames.Admin);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        RecordCreation(ctx, 0, 0, 0, admin.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.PendingVerification);

        var results = await Task.WhenAll(
            WithServiceAsync(admin.Id, RoleNames.Admin, s => s.RejectAsync(land.Id, "race-reason-a")),
            WithServiceAsync(admin.Id, RoleNames.Admin, s => s.RejectAsync(land.Id, "race-reason-b")));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "ParallelRejectAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var rejected = await verify.LandStatusHistories
            .Where(h => h.LandId == land.Id && h.Status == LandStatus.Rejected)
            .ToListAsync();

        Program.Check(results.Count(r => r is null) == 1 && results.Count(r => r is BusinessRuleException) == 1,
            "parallel reject has exactly one winner and one controlled conflict");
        Program.Check(final.Status == LandStatus.Rejected && rejected.Count == 1 &&
            rejected[0].ChangedById == admin.Id &&
            rejected[0].Reason is "race-reason-a" or "race-reason-b",
            "parallel reject writes one Rejected history row with the winner's actor and reason");
    }

    private static async Task StaleUpdateAfterSubmitAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.Draft, withRequiredDocument: true);

        await using (var submitDb = SqlAcceptance.Context())
        {
            var submit = new LandService(submitDb, new TestUser(owner.Id, RoleNames.Landlord), null!, new TestEnvironment());
            await submit.SubmitAsync(land.Id);
        }

        await using var beforeDb = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "StaleUpdateAfterSubmitAsync");
        var before = await beforeDb.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var stale = await WithServiceAsync(owner.Id, RoleNames.Landlord,
            s => s.UpdateAsync(land.Id, EditOf(before, "stale-after-submit")));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "StaleUpdateAfterSubmitAsync verify");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var pending = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.PendingVerification);

        Program.Check(stale is BusinessRuleException,
            "stale update after submit is a controlled conflict");
        Program.Check(final.Status == LandStatus.PendingVerification &&
            final.Description == before.Description && pending == 1,
            "stale update after submit changes neither the reviewed data nor the history");
    }

    private static async Task SequentialUpdateThenSubmitAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.Draft, withRequiredDocument: true);
        var edit = EditOf(land, "sequential");

        var update = await WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.UpdateAsync(land.Id, edit));
        var submit = await WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.SubmitAsync(land.Id));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "SequentialUpdateThenSubmitAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var pending = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.PendingVerification);

        Program.Check(update is null && submit is null,
            "valid sequential update then submit both succeed");
        Program.Check(final.Status == LandStatus.PendingVerification &&
            final.Description == "sequential" && pending == 1,
            "valid sequential update then submit keeps the edit and writes one history row");
    }

    private static async Task SubmitAndUpdateRaceAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.Draft, withRequiredDocument: true);
        var edit = EditOf(land, "after");

        var update = WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.UpdateAsync(land.Id, edit));
        var submit = WithServiceAsync(owner.Id, RoleNames.Landlord, s => s.SubmitAsync(land.Id));
        var results = await Task.WhenAll(update, submit);

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "SubmitAndUpdateRaceAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var pending = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.PendingVerification);

        Program.Check(results[1] is null && (results[0] is null || results[0] is BusinessRuleException),
            "submit always wins the update/submit race, never the reverse");
        Program.Check(final.Status == LandStatus.PendingVerification && pending == 1,
            "update/submit race writes exactly one PendingVerification history row");
        Program.Check(results[0] is null ? final.Description == "after" : final.Description == "before",
            "update/submit race preserves the outcome of the winner without a lost post-submit edit");
    }

    private static async Task FailedVerificationLeavesNoPartialStateAsync(RunContext ctx)
    {
        await using var setup = SqlAcceptance.Context();
        var owner = await SqlAcceptance.FixtureAsync(setup, true);
        var admin = await SqlAcceptance.FixtureAsync(setup, true, RoleNames.Admin);
        RecordCreation(ctx, 0, 0, 0, owner.Id);
        RecordCreation(ctx, 0, 0, 0, admin.Id);
        var land = await CreateLandAsync(setup, ctx, owner.Id, LandStatus.PendingVerification);

        var failed = await WithServiceAsync(admin.Id, RoleNames.Admin, s => s.VerifyAsync(land.Id));

        await using var verify = SqlAcceptance.Context();
        VerifyLandOwnership(ctx, land.Id, "FailedVerificationLeavesNoPartialStateAsync");
        var final = await verify.Lands.AsNoTracking().SingleAsync(l => l.Id == land.Id);
        var verified = await verify.LandStatusHistories
            .CountAsync(h => h.LandId == land.Id && h.Status == LandStatus.Verified);

        Program.Check(failed is BusinessRuleException,
            "failed verification is a controlled conflict");
        Program.Check(final.Status == LandStatus.PendingVerification && verified == 0 &&
            !final.DataVerifiedByAdmin && final.VerifiedAgainstCriterionId is null,
            "failed verification rolls back without a status, history or evidence change");
    }
}