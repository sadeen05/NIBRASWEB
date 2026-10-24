using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using NIBRAS;
using NIBRAS.API.Services;

namespace NibrasWeb.F1.Preparation;

/// <summary>
/// Offline checks for the startup hardening introduced in the production-readiness pass. These
/// checks construct no host, open no database and touch no file system path beyond pure path
/// resolution.
/// </summary>
public static class ProductionHardeningChecks
{
    public static void Run()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "nibras-hardening-static");
        var outsideStorage = Path.Combine(Path.GetTempPath(), "nibras-hardening-storage");
        var production = new TestEnvironment
        {
            EnvironmentName = "Production",
            ContentRootPath = contentRoot,
        };
        var development = new TestEnvironment
        {
            EnvironmentName = "Development",
            ContentRootPath = contentRoot,
        };

        JwtConfigurationChecks();

        var storageValidator = new DocumentStorageOptionsValidator(production);
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = "" }).Failed,
            "production document storage rejects the App_Data fallback at startup");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = "App_Data/land-documents" }).Failed,
            "production document storage rejects a relative root at startup");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = Path.Combine(contentRoot, "wwwroot") }).Failed,
            "document storage rejects a root equal to wwwroot");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = Path.Combine(contentRoot, "wwwroot", "documents") }).Failed,
            "document storage rejects a root inside wwwroot");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = outsideStorage }).Succeeded,
            "production document storage accepts an explicit fully qualified private root");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = outsideStorage, MaxFileSizeBytes = 0 }).Failed,
            "document storage validates the maximum file size at startup");
        Program.Check(
            storageValidator.Validate(null, new DocumentStorageOptions
            {
                StorageRoot = outsideStorage,
                MaxFileSizeBytes = DocumentStorageOptions.DefaultMaxFileSizeBytes + 1,
            }).Failed,
            "document storage rejects a maximum file size above the hard bound");

        var developmentStorageValidator = new DocumentStorageOptionsValidator(development);
        Program.Check(
            developmentStorageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = "" }).Succeeded,
            "Development keeps the App_Data fallback");
        Program.Check(
            developmentStorageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = "App_Data/land-documents" }).Succeeded,
            "Development keeps its relative-root compatibility");
        Program.Check(
            developmentStorageValidator.Validate(null, new DocumentStorageOptions { StorageRoot = Path.Combine(contentRoot, "wwwroot", "documents") }).Failed,
            "Development still rejects a storage root inside wwwroot");

        var reverseValidator = new ReverseProxyOptionsValidator();
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true }).Failed,
            "enabled forwarded headers require an explicit proxy or network");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownProxies = new[] { "*" } }).Failed,
            "forwarded headers reject a wildcard proxy");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownProxies = new[] { "0.0.0.0" } }).Failed,
            "forwarded headers reject the any-address proxy");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownProxies = new[] { "not-an-ip" } }).Failed,
            "forwarded headers reject a malformed proxy");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownNetworks = new[] { "0.0.0.0/0" } }).Failed,
            "forwarded headers reject a network covering the whole IPv4 space");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownNetworks = new[] { "::/0" } }).Failed,
            "forwarded headers reject a network covering the whole IPv6 space");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownNetworks = new[] { "10.0.0.0/33" } }).Failed,
            "forwarded headers reject a malformed network");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions
            {
                Enabled = true,
                KnownProxies = new[] { "10.0.0.10" },
                ForwardLimit = 0,
            }).Failed,
            "forwarded headers reject an invalid hop count");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions
            {
                Enabled = true,
                KnownProxies = new[] { "10.0.0.10" },
                KnownNetworks = new[] { "10.0.0.0/8" },
            }).Succeeded,
            "explicit proxy addresses and networks are accepted");
        Program.Check(
            reverseValidator.Validate(null, new ReverseProxyOptions { Enabled = false }).Succeeded,
            "forwarded headers are disabled by default");

        var forwarded = ReverseProxyConfiguration.Create(new ReverseProxyOptions
        {
            Enabled = true,
            KnownProxies = new[] { "10.0.0.10" },
            KnownNetworks = new[] { "10.0.0.0/8" },
            ForwardLimit = 2,
        });
        Program.Check(
            forwarded.ForwardedHeaders == (ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto),
            "forwarded configuration trusts only the for and proto headers");
        Program.Check(
            forwarded.KnownProxies.Count == 1 &&
            forwarded.KnownIPNetworks.Count == 1 &&
            forwarded.KnownProxies[0].Equals(System.Net.IPAddress.Parse("10.0.0.10")) &&
            forwarded.KnownIPNetworks[0].PrefixLength == 8,
            "forwarded configuration clears framework defaults and trusts only explicit entries");
        Program.Check(forwarded.ForwardLimit == 2, "forwarded configuration applies the explicit hop count");

        Program.Check(
            DatabaseTargetGuard.IsAlwaysForbidden("NEBRAS") &&
            DatabaseTargetGuard.IsAlwaysForbidden("NEBRASDB_Step1") &&
            !DatabaseTargetGuard.IsAlwaysForbidden("NIBRAS_F1_Verify"),
            "the database guard always refuses the historical/shared database names");
        Program.Check(
            DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NIBRAS_F1_Verify;Integrated Security=True;TrustServerCertificate=True",
                null).Database == "NIBRAS_F1_Verify",
            "the database guard accepts the explicitly approved disposable target");
        ExpectInvalidOperation(
            () => DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NEBRAS;Integrated Security=True;TrustServerCertificate=True",
                null),
            "the database guard refuses NEBRAS without opening a connection");
        ExpectInvalidOperation(
            () => DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NEBRASDB_Step1;Integrated Security=True;TrustServerCertificate=True",
                "NEBRASDB_Step1"),
            "the database guard refuses NEBRASDB_Step1 even if listed for approval");
        ExpectInvalidOperation(
            () => DatabaseTargetGuard.Validate(
                "Server=example.test;Database=NEBRASDB;Integrated Security=True;TrustServerCertificate=True",
                null),
            "the database guard refuses unapproved NEBRASDB");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "api.example.test;admin.example.test",
            })
            .Build();
        ProductionConfigurationGuard.Validate(production, configuration);
        Program.Check(true, "explicit production host names pass the deployment guard");

        ExpectInvalidOperation(
            () => ProductionConfigurationGuard.Validate(
                production,
                Configuration(("AllowedHosts", "*"))),
            "production AllowedHosts wildcard is refused");
        ExpectInvalidOperation(
            () => ProductionConfigurationGuard.Validate(
                production,
                Configuration(("AllowedHosts", ""))),
            "production AllowedHosts must not be empty");
        ExpectInvalidOperation(
            () => ProductionConfigurationGuard.Validate(
                production,
                Configuration(
                    ("AllowedHosts", "api.example.test"),
                    ("Seed:SuperAdminEmail", "synthetic@example.test"),
                    ("Seed:SuperAdminPassword", "synthetic-password"))),
            "production startup refuses Development-only SuperAdmin seed credentials");

        ProductionConfigurationGuard.Validate(
            development,
            Configuration(
                ("AllowedHosts", "*"),
                ("Seed:SuperAdminEmail", "synthetic@example.test"),
                ("Seed:SuperAdminPassword", "synthetic-password")));
        Program.Check(true, "Development keeps the local seed and wildcard-host behavior");

        CleanDatabaseChecks.Run();
    }

    private static void JwtConfigurationChecks()
    {
        var validKey = Convert.ToBase64String(new byte[32]);
        var complete = JwtConfiguration.Read(Configuration(
            ("Jwt:Key", validKey),
            ("Jwt:Issuer", "nibras-api"),
            ("Jwt:Audience", "nibras-web"),
            ("Jwt:ExpiryMinutes", "120")));
        Program.Check(
            complete.Key.Length == 32 && complete.Issuer == "nibras-api" && complete.Audience == "nibras-web",
            "JWT configuration accepts a complete key, issuer, audience and lifetime");

        ExpectInvalidOperation(
            () => JwtConfiguration.Read(Configuration(
                ("Jwt:Issuer", "nibras-api"),
                ("Jwt:Audience", "nibras-web"),
                ("Jwt:ExpiryMinutes", "120"))),
            "JWT configuration rejects a missing signing key");

        ExpectInvalidOperation(
            () => JwtConfiguration.Read(Configuration(
                ("Jwt:Key", validKey),
                ("Jwt:Audience", "nibras-web"),
                ("Jwt:ExpiryMinutes", "120"))),
            "JWT configuration rejects a missing issuer");

        ExpectInvalidOperation(
            () => JwtConfiguration.Read(Configuration(
                ("Jwt:Key", validKey),
                ("Jwt:Issuer", "nibras-api"),
                ("Jwt:ExpiryMinutes", "120"))),
            "JWT configuration rejects a missing audience");

        ExpectInvalidOperation(
            () => JwtConfiguration.Read(Configuration(
                ("Jwt:Key", validKey),
                ("Jwt:Issuer", "nibras-api"),
                ("Jwt:Audience", "nibras-web"),
                ("Jwt:ExpiryMinutes", "0"))),
            "JWT configuration rejects a non-positive lifetime");
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

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
