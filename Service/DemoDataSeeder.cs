using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Step 7.5: Development-only, opt-in demo data seeder (extends the Step 3 SuperAdmin
/// seeder). It runs only when the host environment is Development, <c>Seed:DemoData</c> is
/// exactly "true", and <c>Seed:DemoPassword</c> holds at least 10 characters; otherwise it
/// logs one line and skips. The password is read at runtime from configuration and only its
/// BCrypt hash is ever persisted. Every record is matched on a natural key, so repeat runs
/// neither duplicate nor overwrite existing data. All identifiers, phone numbers and
/// document paths are clearly fictional ("DEMO ...").
/// <para>
/// Demo offer: intentionally NOT seeded. <see cref="NibrasWeb.Service.OfferService.CreateAsync"/>
/// takes the acting identity from the token (<c>RequireUserId</c> via ICurrentUser /
/// IHttpContextAccessor), so it cannot run at startup, and reproducing it here would
/// duplicate its business logic: the owner/role/approval gates, the ConnectionMechanism
/// check, the land UPDLOCK + fresh Verified-status check inside a transaction (BR-O1/Step 5
/// M-1), the one-active-offer rule (BR-O2), the Offer/OfferVersion/OfferNegotiationHistory
/// construction, and the UX_OFFERS_ACTIVE_INVESTOR_LAND unique-violation handling. That
/// logic is deliberately left to OfferService and is not duplicated here.
/// </para>
/// </summary>
public static class DemoDataSeeder
{
    private const string DemoDataKey = "Seed:DemoData";
    private const string DemoPasswordKey = "Seed:DemoPassword";
    private const int MinimumPasswordLength = 10;
    private const int BCryptWorkFactor = 12;

    private const string RegionNameEn = "DEMO Region - Northern Valley";
    private const string RegionNameAr = "DEMO Region - Northern Valley";
    private const string GridName = "DEMO Grid - Northern Valley 50MW";
    private const decimal GridCapacityMw = 50m;

    private const string DemoVillage = "DEMO Village";
    private const string DemoBasin = "DEMO Basin";
    private const string DraftLandNumber = "DEMO-LAND-001";
    private const string PendingLandNumber = "DEMO-LAND-002";
    private const string VerifiedLandNumber = "DEMO-LAND-003";

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");

        if (!environment.IsDevelopment())
            return;

        if (!string.Equals(configuration[DemoDataKey], "true", StringComparison.Ordinal))
        {
            logger.LogInformation("Demo data seeding skipped: set {Key} to exactly 'true' to enable it.", DemoDataKey);
            return;
        }

        var password = configuration[DemoPasswordKey];
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
        {
            logger.LogInformation(
                "Demo data seeding skipped: {Key} must be configured with at least {Minimum} characters.",
                DemoPasswordKey,
                MinimumPasswordLength);
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NebrasdbContext>();
        var now = DateTime.UtcNow;

        // Demo data must never be written to the clean official database (or the legacy
        // production-like database). This refusal is independent of the caller.
        DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase(
            context.Database.GetConnectionString(),
            nameof(DemoDataSeeder));

        var roleIds = await context.Roles.AsNoTracking().ToDictionaryAsync(r => r.Name, r => r.Id);
        if (!roleIds.TryGetValue(RoleNames.Admin, out var adminRoleId) ||
            !roleIds.TryGetValue(RoleNames.Landlord, out var landlordRoleId) ||
            !roleIds.TryGetValue(RoleNames.Investor, out var investorRoleId))
        {
            logger.LogWarning("Demo data seeding skipped: the required roles are not present.");
            return;
        }

        // The shared demo password is hashed once and only the hash is stored.
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: BCryptWorkFactor);

        var region = await EnsureRegionAsync(context);
        var grid = await EnsureGridAsync(context, region.Id);

        // Role and approval conventions follow the model: landlords and admins are approved,
        // one investor is approved and the other is left pending (as at registration).
        var admin = await EnsureUserAsync(context, adminRoleId, "DEMO Admin", "demo-admin@nibras.demo", "DEMO-PHONE-01", passwordHash, true, now);
        var landlord1 = await EnsureUserAsync(context, landlordRoleId, "DEMO Landlord One", "demo-landlord1@nibras.demo", "DEMO-PHONE-02", passwordHash, true, now);
        await EnsureUserAsync(context, landlordRoleId, "DEMO Landlord Two", "demo-landlord2@nibras.demo", "DEMO-PHONE-03", passwordHash, true, now);
        await EnsureUserAsync(context, investorRoleId, "DEMO Investor Approved", "demo-investor@nibras.demo", "DEMO-PHONE-04", passwordHash, true, now);
        await EnsureUserAsync(context, investorRoleId, "DEMO Investor Pending", "demo-investor-pending@nibras.demo", "DEMO-PHONE-05", passwordHash, false, now);

        // "Current" criteria is the greatest Id (see CriteriaService/LandService), never UpdatedAt.
        var criterion = await context.LandCriteria
            .AsNoTracking()
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();
        if (criterion is null)
        {
            logger.LogWarning("Demo data: land seeding skipped because no eligibility criteria are configured.");
            return;
        }

        // Three lands, all owned by landlord1, spanning the verification journey. Field values
        // satisfy the current criterion (min area/slope/distance/irradiance; elevation optional).
        var draftLand = await EnsureLandAsync(
            context, region.Id, grid.Id, landlord1.Id, null,
            DraftLandNumber, "DEMO-PARCEL-001", 85m, 6m, 11m, 5.8m, 280m, LandStatus.Draft);

        var pendingLand = await EnsureLandAsync(
            context, region.Id, grid.Id, landlord1.Id, null,
            PendingLandNumber, "DEMO-PARCEL-002", 95m, 5.5m, 10m, 6m, 300m, LandStatus.PendingVerification);

        var verifiedLand = await EnsureLandAsync(
            context, region.Id, grid.Id, landlord1.Id, criterion.Id,
            VerifiedLandNumber, "DEMO-PARCEL-003", 120m, 4.5m, 9m, 6.2m, 320m, LandStatus.Verified);

        // Pending land: both required documents uploaded, awaiting review.
        await EnsureLandDocumentsAsync(context, pendingLand.Id, PendingLandNumber, DocumentStatuses.Pending, null, now);

        // Verified land: both required documents approved by the demo admin.
        await EnsureLandDocumentsAsync(context, verifiedLand.Id, VerifiedLandNumber, DocumentStatuses.Approved, admin.Id, now);

        await EnsureLandHistoryAsync(context, draftLand.Id, landlord1.Id, admin.Id, now,
            new[] { LandStatus.Draft });
        await EnsureLandHistoryAsync(context, pendingLand.Id, landlord1.Id, admin.Id, now,
            new[] { LandStatus.Draft, LandStatus.PendingVerification });
        await EnsureLandHistoryAsync(context, verifiedLand.Id, landlord1.Id, admin.Id, now,
            new[] { LandStatus.Draft, LandStatus.PendingVerification, LandStatus.Verified });

        logger.LogInformation(
            "Demo data seeding completed: region '{Region}', grid '{Grid}', 5 demo users, 3 demo lands (1 draft, 1 pending, 1 verified).",
            RegionNameEn,
            GridName);
    }

    private static async Task<Region> EnsureRegionAsync(NebrasdbContext context)
    {
        var existing = await context.Regions.FirstOrDefaultAsync(r => r.NameEn == RegionNameEn);
        if (existing is not null)
            return existing;

        var region = new Region
        {
            NameEn = RegionNameEn,
            NameAr = RegionNameAr,
            PeakSunHoursPerDay = 5.6m,
            WheelingFeePerKwh = 0.021m,
            LossPercentage = 3.5m
        };

        context.Regions.Add(region);
        await context.SaveChangesAsync();
        return region;
    }

    private static async Task<Grid> EnsureGridAsync(NebrasdbContext context, int regionId)
    {
        var existing = await context.Grids.FirstOrDefaultAsync(g => g.Name == GridName);
        if (existing is not null)
            return existing;

        var grid = new Grid
        {
            RegionId = regionId,
            Name = GridName,
            CapacityMw = GridCapacityMw,
            Status = "Operational"
        };

        context.Grids.Add(grid);
        await context.SaveChangesAsync();
        return grid;
    }

    private static async Task<User> EnsureUserAsync(
        NebrasdbContext context,
        int roleId,
        string fullName,
        string email,
        string phone,
        string passwordHash,
        bool isApproved,
        DateTime now)
    {
        // IgnoreQueryFilters: a soft-deleted row still owns the unique EMAIL/PHONE value.
        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
            return existing;

        var user = new User
        {
            RoleId = roleId,
            FullName = fullName,
            Email = email,
            Phone = phone,
            PasswordHash = passwordHash,
            IsDeleted = false,
            IsApproved = isApproved,
            ApprovedById = null,
            ApprovedAt = isApproved ? now : null,
            CreatedAt = now
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<Land> EnsureLandAsync(
        NebrasdbContext context,
        int regionId,
        int gridId,
        int landlordId,
        int? criterionId,
        string landNumber,
        string parcelNumber,
        decimal areaDonum,
        decimal slopePercentage,
        decimal distanceToGridKm,
        decimal solarIrradiance,
        decimal elevationM,
        LandStatus status)
    {
        var existing = await context.Lands
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.LandNumber == landNumber && l.RegionId == regionId);
        if (existing is not null)
            return existing;

        var land = new Land
        {
            LandlordId = landlordId,
            RegionId = regionId,
            GridId = gridId,
            LandNumber = landNumber,
            Village = DemoVillage,
            Basin = DemoBasin,
            ParcelNumber = parcelNumber,
            AreaDonum = areaDonum,
            SlopePercentage = slopePercentage,
            DistanceToGridKm = distanceToGridKm,
            SolarIrradiance = solarIrradiance,
            ElevationM = elevationM,
            IsDeleted = false,
            Status = status,
            DataVerifiedByAdmin = status == LandStatus.Verified,
            DocumentStorageLocation = "demo/docs/" + landNumber,
            VerifiedAgainstCriterionId = criterionId
        };

        context.Lands.Add(land);
        await context.SaveChangesAsync();
        return land;
    }

    private static async Task EnsureLandDocumentsAsync(
        NebrasdbContext context,
        int landId,
        string landNumber,
        string documentStatus,
        int? reviewerId,
        DateTime now)
    {
        var documentTypes = await context.DocumentTypes
            .AsNoTracking()
            .Where(d => d.IsRequiredForVerification)
            .OrderBy(d => d.Id)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();

        foreach (var documentType in documentTypes)
        {
            var exists = await context.LandDocuments.AnyAsync(d =>
                d.LandId == landId && d.DocumentTypeId == documentType.Id && d.Version == 1);
            if (exists)
                continue;

            context.LandDocuments.Add(new LandDocument
            {
                LandId = landId,
                DocumentTypeId = documentType.Id,
                FilePath = $"demo/docs/{landNumber}/{documentType.Name}.pdf",
                Version = 1,
                Status = documentStatus,
                UploadedAt = now,
                ReviewedById = reviewerId,
                ReviewedAt = reviewerId is null ? null : now,
                ReviewNote = reviewerId is null ? null : "DEMO document approved by the demo seeder."
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task EnsureLandHistoryAsync(
        NebrasdbContext context,
        int landId,
        int landlordId,
        int adminId,
        DateTime now,
        LandStatus[] journey)
    {
        var dayOffset = -journey.Length;

        foreach (var status in journey)
        {
            var exists = await context.LandStatusHistories.AnyAsync(h =>
                h.LandId == landId && h.Status == status);
            if (exists)
            {
                dayOffset++;
                continue;
            }

            var (changedById, reason) = status switch
            {
                LandStatus.Draft => (landlordId, "DEMO land created."),
                LandStatus.PendingVerification => (landlordId, "DEMO land submitted for verification."),
                LandStatus.Verified => (adminId, "DEMO land verified by admin."),
                _ => (landlordId, "DEMO land status changed.")
            };

            context.LandStatusHistories.Add(new LandStatusHistory
            {
                LandId = landId,
                Status = status,
                ChangedById = changedById,
                Reason = reason,
                ChangedAt = now.AddDays(dayOffset)
            });

            dayOffset++;
        }

        await context.SaveChangesAsync();
    }
}
