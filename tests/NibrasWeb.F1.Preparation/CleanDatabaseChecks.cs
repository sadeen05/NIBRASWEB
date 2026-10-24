using Microsoft.EntityFrameworkCore;
using NIBRAS;

namespace NibrasWeb.F1.Preparation;

/// <summary>
/// Offline checks for the clean-database target guard, the production-like protection and the
/// model migration set. These checks construct no host, open no database, never run the
/// clean-database runner and never touch a SQL Server instance.
/// </summary>
internal static class CleanDatabaseChecks
{
    public static void Run()
    {
        // --- the exact clean target is accepted ---
        Program.Check(
            CleanDatabaseTargetGuard.Validate(CleanConnection("SADEEN\\SQLEXPRESS", "NIBRAS_cleanvirsion")).Database
                == "NIBRAS_cleanvirsion",
            "clean-database target guard accepts the exact server and database");

        // --- every other database is refused ---
        foreach (var database in new[]
                 {
                     "NEBRASDB", "NIBRAS_F1_Verify", "NEBRAS", "NEBRASDB_Step1",
                     "NIBRAS_cleanvirsion_extra", "NIBRAS_cleanvirsio",
                 })
        {
            ExpectInvalidOperation(
                () => CleanDatabaseTargetGuard.Validate(CleanConnection("SADEEN\\SQLEXPRESS", database)),
                $"clean-database target guard refuses database '{database}'");
        }

        // --- every other server is refused ---
        foreach (var server in new[] { "OTHERHOST\\SQLEXPRESS", "SADEEN", "SADEEN\\OTHER", "SADEEN\\SQLEXPRESS,1433" })
        {
            ExpectInvalidOperation(
                () => CleanDatabaseTargetGuard.Validate(CleanConnection(server, "NIBRAS_cleanvirsion")),
                $"clean-database target guard refuses server '{server}'");
        }

        // --- missing / ambiguous connection strings fail closed ---
        ExpectInvalidOperation(
            () => CleanDatabaseTargetGuard.Validate(null),
            "clean-database target guard refuses a missing connection string");
        ExpectInvalidOperation(
            () => CleanDatabaseTargetGuard.Validate(
                "Server=SADEEN\\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Initial Catalog=NIBRAS_cleanvirsion"),
            "clean-database target guard refuses an ambiguous connection string");

        // --- production-like protection: the clean database is protected without unprotecting NEBRASDB ---
        Program.Check(DatabaseTargetGuard.IsProductionLike("NEBRASDB"), "the legacy NEBRASDB remains production-like");
        Program.Check(DatabaseTargetGuard.IsProductionLike("NIBRAS_cleanvirsion"), "the clean database is production-like");
        Program.Check(!DatabaseTargetGuard.IsProductionLike("NIBRAS_F1_Verify"), "the disposable test target stays non-production-like");
        Program.Check(!DatabaseTargetGuard.IsAlwaysForbidden("NIBRAS_cleanvirsion"), "the clean database is protected, not always-forbidden");
        Program.Check(
            DatabaseTargetGuard.IsAlwaysForbidden("NEBRAS") && DatabaseTargetGuard.IsAlwaysForbidden("NEBRASDB_Step1"),
            "the historical/shared names stay always-forbidden");

        ExpectInvalidOperation(
            () => DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True",
                null),
            "the application guard refuses unapproved NIBRAS_cleanvirsion");
        Program.Check(
            DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True",
                "NIBRAS_cleanvirsion").Database == "NIBRAS_cleanvirsion",
            "the application guard accepts NIBRAS_cleanvirsion when explicitly approved");
        Program.Check(
            DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NEBRASDB;Integrated Security=True;TrustServerCertificate=True",
                "NEBRASDB").Database == "NEBRASDB",
            "the legacy NEBRASDB approval is preserved");

        // --- Development seeding is refused on protected databases ---
        ExpectInvalidOperation(
            () => DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase(
                CleanConnection("SADEEN\\SQLEXPRESS", "NIBRAS_cleanvirsion"),
                "SuperAdminSeeder"),
            "Development seeding is refused against the clean database");
        ExpectInvalidOperation(
            () => DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase(
                "Server=example.test;Database=NEBRASDB;Integrated Security=True",
                "DemoDataSeeder"),
            "Development seeding is refused against the legacy production-like database");
        DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase(
            "Server=example.test;Database=NIBRAS_F1_Verify;Integrated Security=True",
            "SuperAdminSeeder");
        Program.Check(true, "Development seeding stays allowed on the disposable test target");

        // --- the model migration set is preserved (offline, no connection) ---
        using var model = new ModelOnlyFactory().CreateDbContext([]);
        var migrations = model.Database.GetMigrations().ToList();
        Program.Check(migrations.Count == 21, "the model defines exactly 21 migrations");
        Program.Check(
            migrations[^1] == "20261023173135_HardenLandLocationUniqueness",
            "the final migration is the land-location hardening");
        Program.Check(
            migrations.Distinct(StringComparer.Ordinal).Count() == migrations.Count,
            "migration identifiers are unique");
    }

    private static string CleanConnection(string server, string database) =>
        $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True";

    private static void ExpectInvalidOperation(Action action, string label)
    {
        var failed = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        Program.Check(failed, label);
    }
}
