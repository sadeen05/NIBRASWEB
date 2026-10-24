using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public static class SuperAdminSeeder
{
    /// <summary>
    /// The phone number reserved for the SuperAdmin identity (Development seed and the
    /// production out-of-band bootstrap alike). It is a constant on purpose: the identity
    /// must be recognisable, and a bootstrap refuses to reuse or change an account that
    /// already owns the number instead of failing with a raw unique constraint violation.
    /// </summary>
    public const string ReservedPhone = "9999999999";

    private const string SeedPhone = ReservedPhone;

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var email = configuration["Seed:SuperAdminEmail"];
        var password = configuration["Seed:SuperAdminPassword"];
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SuperAdminSeeder");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("SuperAdmin seeding skipped because required configuration is missing.");
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NebrasdbContext>();

        // The clean official database (and the legacy production-like database) must never
        // receive Development seed data. This refusal is independent of the caller.
        DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase(
            context.Database.GetConnectionString(),
            nameof(SuperAdminSeeder));

        var superAdminRole = await context.Roles
            .SingleAsync(role => role.Name == RoleNames.SuperAdmin);

        // Idempotent by the configured email (USERS has a unique index on EMAIL). Holding the
        // SuperAdmin role is NOT proof that the configured administrator exists: the model
        // seeds an unusable placeholder (system@nibras.internal, PasswordHash "N/A"), which
        // previously short-circuited this seeder and left no login-capable SuperAdmin.
        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Email == email);

        if (existing is not null)
        {
            // The configured identity already exists, and it is left exactly as it is: this
            // seeder never changes an email, a password, a role, a phone number or an approval
            // state. When the account is not the approved, active SuperAdmin the identity
            // promises, the seeding FAILS CLOSED instead of continuing silently, because the
            // operator would otherwise believe the seeded administrator is in place.
            if (existing.RoleId != superAdminRole.Id || !existing.IsApproved || existing.IsDeleted)
            {
                throw new InvalidOperationException(
                    "The configured SuperAdmin seed identity already belongs to an account that is not an approved, active SuperAdmin. " +
                    "The seeding refuses to change that account; use another seed identity or correct the account deliberately.");
            }

            logger.LogInformation(
                "SuperAdmin seeding skipped: the configured identity is already an approved, active SuperAdmin.");
            return;
        }

        // The reserved phone number is checked before the insert: when another account already
        // owns it, the insert would abort the host with a raw unique-constraint violation, and
        // the seeder must never reuse or change the account that owns it.
        var phoneOwner = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Phone == SeedPhone);

        if (phoneOwner is not null)
        {
            throw new InvalidOperationException(
                "The reserved phone number of the SuperAdmin seed identity is already used by another account. " +
                "The seeding refuses to reuse or change that account; remove the conflicting seed account deliberately or use another database.");
        }

        var superAdmin = new User
        {
            RoleId = superAdminRole.Id,
            FullName = "Super Admin",
            Email = email,
            Phone = SeedPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            IsDeleted = false,
            IsApproved = true,
            ApprovedAt = DateTime.UtcNow
        };

        // One insert in one transaction: the account exists completely or not at all.
        context.Users.Add(superAdmin);
        await context.SaveChangesAsync();
    }
}
