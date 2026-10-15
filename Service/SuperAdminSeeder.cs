using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public static class SuperAdminSeeder
{
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
            if (existing.RoleId != superAdminRole.Id || !existing.IsApproved || existing.IsDeleted)
            {
                logger.LogWarning(
                    "A user already owns the configured SuperAdmin email but is not an approved, active SuperAdmin; leaving it unchanged.");
            }

            return;
        }

        var superAdmin = new User
        {
            RoleId = superAdminRole.Id,
            FullName = "Super Admin",
            Email = email,
            Phone = "9999999999",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            IsDeleted = false,
            IsApproved = true,
            ApprovedAt = DateTime.UtcNow
        };

        context.Users.Add(superAdmin);
        await context.SaveChangesAsync();
    }
}
