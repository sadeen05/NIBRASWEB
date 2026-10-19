
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using NIBRAS;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Service;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576;
});

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("JWT signing key is missing. Configure Jwt:Key in User Secrets or environment variables.");
}

byte[] jwtKeyBytes;
try
{
    jwtKeyBytes = Convert.FromBase64String(jwtKey);
}
catch (FormatException ex)
{
    throw new InvalidOperationException("JWT signing key must be valid Base64.", ex);
}

if (jwtKeyBytes.Length < 32)
{
    throw new InvalidOperationException("JWT signing key must decode to at least 32 bytes.");
}

builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int>("RateLimit:AuthPerMinute"),
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
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
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

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

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

app.Run();
