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

        if (await context.Users.AnyAsync(user => user.RoleId == superAdminRole.Id))
        {
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
