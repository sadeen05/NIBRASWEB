
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NIBRAS;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Service;

var builder = WebApplication.CreateBuilder(args);

// SEC-14: every endpoint keeps the 1 MiB outer request limit. The multipart document upload
// raises it for that one action only (see LandsController), so a large upload cannot enlarge
// the request budget of the rest of the API.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576;
});

var (jwtKeyBytes, jwtIssuer, jwtAudience) = JwtConfiguration.Read(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:AuthPerMinute") ?? 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // The admin activation endpoint has its own budget, so that a flood of activation attempts
    // can never exhaust the sign-in and registration budget of the same address. The limit has a
    // safe default, so the endpoint is never blocked by a missing configuration value.
    options.AddPolicy("activation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:ActivationPerMinute") ?? 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // The confirmation of an address has its own budget as well: a flood of confirmation
    // attempts must never exhaust the sign-in budget, and the confirmation of one address must
    // not be blocked by the attempts on another one.
    options.AddPolicy("verification", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:VerificationPerMinute") ?? 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // The request for a new verification mail is the only anonymous endpoint that can cause
    // mail to be sent, so it gets its own, smaller budget on top of the per-account cooldown
    // and the per-account daily cap of the token service.
    options.AddPolicy("verification-resend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:VerificationResendPerMinute") ?? 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // An authenticated address change can also cause a mail to be sent, so it gets a small
    // budget independent of the sign-in and resend budgets.
    options.AddPolicy("email-change", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:EmailChangePerMinute") ?? 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddDbContext<NebrasdbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                if (!int.TryParse(subject, out var userId) || string.IsNullOrWhiteSpace(role))
                {
                    context.Fail("Token identity is invalid.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<NebrasdbContext>();
                var user = await db.Users
                    .IgnoreQueryFilters()
                    .Include(u => u.Role)
                    .SingleOrDefaultAsync(u => u.Id == userId);
                if (user is null || user.IsDeleted || !string.Equals(user.Role.Name, role, StringComparison.Ordinal))
                {
                    context.Fail("Token user is no longer valid.");
                    return;
                }

                // An account whose address has not been confirmed may not use a session that
                // was issued before the restriction existed: the token is rejected, so a
                // session can never bypass the sign-in gate. A legacy account (a null
                // verification flag) counts as verified and keeps its access. The rejection is
                // not audited: it would repeat on every request of a session, and the sign-in
                // attempt itself is already recorded.
                if (user.EmailVerified is false)
                {
                    context.Fail("Token user is no longer valid.");
                    return;
                }

                // A confirmed address change replaces the active address and updates
                // EmailVerifiedAt. The session carries the address and the verification epoch it
                // was issued for, so every session issued before the change is rejected instead
                // of keeping access to the old identity. A token issued by an older build has no
                // epoch claim and is therefore rejected as well; signing in again issues a
                // current one.
                var tokenEmail = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                if (!string.Equals(tokenEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Token session is stale.");
                    return;
                }

                var tokenEpoch = context.Principal?.FindFirst(AuthClaimTypes.EmailVerifiedAt)?.Value;
                var currentEpoch = user.EmailVerifiedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(tokenEpoch, currentEpoch, StringComparison.Ordinal))
                {
                    context.Fail("Token session is stale.");
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IGovernorateService, GovernorateService>();
builder.Services.AddScoped<IOfferService, OfferService>();
builder.Services.AddScoped<IGridService, GridService>();
builder.Services.AddScoped<ILandService, LandService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<ICriteriaService, CriteriaService>();
builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<IGridCapacityReservationService, GridCapacityReservationService>();

// Private land-document storage. The root is never under wwwroot; the service validates the
// file signature and size, and the controller serves the bytes only through an authenticated
// download endpoint. The root itself is validated at host startup: outside Development it must
// be explicitly configured and fully qualified, so the host cannot start with a path that would
// fail on the first upload.
builder.Services.AddOptions<DocumentStorageOptions>()
    .Bind(builder.Configuration.GetSection(DocumentStorageOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DocumentStorageOptions>>(sp =>
    new DocumentStorageOptionsValidator(sp.GetRequiredService<IHostEnvironment>()));
builder.Services.AddSingleton<IDocumentStorage, DocumentStorageService>();

// Reverse-proxy trust is opt-in and explicit. With the feature disabled, forwarded headers are
// ignored. When enabled, the validator requires known proxies or CIDR networks, and the
// middleware options are built from that exact list; framework loopback defaults are cleared.
builder.Services.AddOptions<ReverseProxyOptions>()
    .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>();

// Email verification support. The delivery mode, the sender and the token lifetimes come from
// configuration only - no address, host, user or password is part of the source. The
// configuration is validated at startup: outside Development the mode must be SMTP, so email
// verification can neither be switched off nor diverted into the development outbox.
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EmailOptions>>(sp =>
    new EmailOptionsValidator(sp.GetRequiredService<IHostEnvironment>()));
builder.Services.AddSingleton<IEmailSender>(sp =>
{
    var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    if (options.IsSmtp)
    {
        return new SmtpEmailSender(Options.Create(options), loggerFactory.CreateLogger<SmtpEmailSender>());
    }

    if (options.IsFileOutbox)
    {
        return new FileOutboxEmailSender(
            Options.Create(options),
            sp.GetRequiredService<IHostEnvironment>(),
            loggerFactory.CreateLogger<FileOutboxEmailSender>());
    }

    return new DisabledEmailSender(loggerFactory.CreateLogger<DisabledEmailSender>());
});
builder.Services.AddScoped<IUserSecurityEventService, UserSecurityEventService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailChangeService, EmailChangeService>();
builder.Services.AddScoped<IAdminInvitationService, AdminInvitationService>();
builder.Services.AddScoped<IRegistrationVerificationService, RegistrationVerificationService>();

// Operational health endpoints. Liveness depends on nothing external; readiness proves the
// essential dependency (the database) is answerable inside a bounded window and never
// produces mail or infrastructure detail. Both are anonymous on purpose: they must be
// reachable by a reverse proxy or monitoring platform without weakening the authorization
// of the API (the fallback policy stays authenticated-only for every other endpoint).
builder.Services.AddOptions<HealthOptions>()
    .Bind(builder.Configuration.GetSection(HealthOptions.SectionName));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessCheck>("database", tags: new[] { HealthOptions.ReadinessTag });

// The verification deliveries run in one managed worker with a bounded queue: the request
// that offers the work is never blocked, the amount of pending work is limited, and the
// worker owns the lifecycle and the shutdown.
builder.Services.AddSingleton<VerificationDeliveryCounters>();
builder.Services.AddSingleton<IVerificationDeliveryQueue, VerificationDeliveryQueue>();
builder.Services.AddHostedService<VerificationDeliveryWorker>();

// A background service that fails fatally must not leave the application running in a
// degraded state - a worker that is gone while the host still answers would accept requests
// that can never be delivered. The host is stopped instead, and the deployment restarts it.
// The behaviour is set explicitly rather than relied upon as the default.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter a JWT bearer token."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// The one-time SuperAdmin bootstrap is an explicitly invoked out-of-band operator command,
// never an automatic path. The invocation is recognized from the process arguments alone;
// in Development the invocation is refused (Development uses its seed configuration), and
// in any environment the host exits with a code instead of starting to listen.
var bootstrapInvocation = SuperAdminBootstrap.IsInvocation(args, out var bootstrapConfirmation);
if (bootstrapInvocation && app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "SuperAdmin bootstrap refused: Development uses the Development-only seed configuration instead.");
    return SuperAdminBootstrap.ExitDevelopmentRefused;
}

// Fail before any database access or seeding when the deployment still relies on permissive
// defaults. AllowedHosts must be explicit outside Development, and the Development-only
// SuperAdmin seed credentials must not be carried into a production-like environment. The
// bootstrap invocation is the only start that may carry its (separately named) credentials.
ProductionConfigurationGuard.Validate(app.Environment, app.Configuration, bootstrapInvocation);

// Resolve the startup-validated options before seeders or database access. This makes a bad
// document root or reverse-proxy trust list stop the host immediately, rather than on the first
// document upload or the first proxied request.
_ = app.Services.GetRequiredService<IOptions<DocumentStorageOptions>>().Value;
var reverseProxyOptions = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;

// Fail fast before any database access or seeding: the resolved target must be
// configured, and the production-like database is denied in every environment
// unless its exact name is explicitly approved through deployment configuration.
var databaseTarget = DatabaseTargetGuard.Validate(
    app.Configuration.GetConnectionString("DefaultConnection"),
    app.Configuration[DatabaseTargetGuard.ApprovedProductionLikeTargetsKey]);
app.Logger.LogInformation(
    "Database target: {Server}/{Database}",
    databaseTarget.Server,
    databaseTarget.Database);

if (bootstrapInvocation)
{
    // The bootstrap exits the process and never starts the host: no endpoint listens, no
    // background service runs, and the database guard above applied exactly as on any
    // other start.
    var bootstrapExit = await SuperAdminBootstrap.ExecuteAsync(
        app.Services,
        app.Configuration,
        bootstrapConfirmation,
        app.Environment.IsDevelopment(),
        CancellationToken.None);
    return bootstrapExit;
}

if (app.Environment.IsDevelopment())
{
    await SuperAdminSeeder.SeedAsync(app.Services, app.Configuration, app.Environment);
    await DemoDataSeeder.SeedAsync(app.Services, app.Configuration, app.Environment);
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var isSwaggerUi = context.Request.Path == "/swagger" ||
                          context.Request.Path.StartsWithSegments("/swagger/index.html");
        context.Response.Headers["Content-Security-Policy"] =
            app.Environment.IsDevelopment() && isSwaggerUi
                ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
                : "default-src 'none'; frame-ancestors 'none'";

        return Task.CompletedTask;
    });

    await next();
});
app.UseExceptionHandler();

// Forwarded headers must run before HTTPS redirection, HSTS and the rate limiter so the
// request scheme and remote address are the values seen by the trusted proxy. The middleware is
// omitted entirely when the feature is disabled, so an unconfigured host never trusts a
// client-supplied forwarding header.
if (reverseProxyOptions.Enabled)
{
    app.UseForwardedHeaders(ReverseProxyConfiguration.Create(reverseProxyOptions));
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ASP.NET Core health middleware answers with plain "Healthy"/"Unhealthy" text and the
// status code only - the default writer never includes descriptions, entries or exception
// data, so no infrastructure detail can leak to a proxied caller.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness runs no dependency check at all: the process answering is the whole signal,
    // so the answer can never depend on the database or SMTP.
    Predicate = _ => false,
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Readiness runs exactly the checks tagged as essential dependencies (currently the
    // bounded database check). A failure answers 503 so the proxy can pull the instance.
    Predicate = check => check.Tags.Contains(HealthOptions.ReadinessTag),
}).AllowAnonymous();

app.Run();
return 0;
