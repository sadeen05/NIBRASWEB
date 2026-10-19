using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

public sealed class AuthService : IAuthService
{
    private const string DummyPasswordHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEe.6QxT5s0L9L8h1f4mM7Q7V9n2M3j7b6W";
    private readonly NebrasdbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IUserSecurityEventService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        NebrasdbContext context,
        IConfiguration configuration,
        IUserSecurityEventService audit,
        ICurrentUser currentUser,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<AuthUserSummary> RegisterAsync(RegisterRequest request)
    {
        PasswordPolicy.Validate(request.Password);

        if (request.RoleId is not (1 or 2))
        {
            throw new BusinessRuleException("Only Landlord and Investor registration is allowed.");
        }

        if (request.RoleId == 2 &&
            (string.IsNullOrWhiteSpace(request.CompanyName) ||
             string.IsNullOrWhiteSpace(request.CompanyRegistrationNumber)))
        {
            throw new ValidationFailedException("Investor company details are required.");
        }

        if (await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => (u.Email == request.Email || u.Phone == request.Phone)))
        {
            throw new DuplicateRegistrationException();
        }

        var role = await _context.Roles.SingleAsync(r => r.Id == request.RoleId);
        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            RoleId = request.RoleId,
            IsApproved = request.RoleId == 1,
            CompanyName = request.CompanyName,
            CompanyRegistrationNumber = request.CompanyRegistrationNumber,
            IsDeleted = false
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return ToSummary(user, role.Name);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(request.Password);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => !u.IsDeleted && u.Email == request.Email);

        if (user is null || user.IsDeleted)
        {
            // The dummy hash keeps the timing of an unknown address comparable to a wrong
            // password. It can never throw, so a failed login stays a failed login.
            PasswordVerifier.TryVerify(request.Password, DummyPasswordHash);
            return null;
        }

        // An unusable stored hash (for example the placeholder of the seeded system account)
        // can never match, so it answers the same generic 401 as a wrong password instead of
        // surfacing as a server error.
        if (!PasswordVerifier.TryVerify(request.Password, user.PasswordHash))
        {
            return null;
        }

        // The address of the account has to be proven before the account may be used. The
        // check comes **after** the password was confirmed, so a wrong password never reveals
        // the verification state of the account, and it refuses only an account that is
        // explicitly unverified: a legacy account (EMAIL_VERIFIED IS NULL) counts as verified
        // and keeps its access. The refusal is recorded in the audit trail without any
        // credential, and the answer carries a stable code the client can act on.
        if (user.EmailVerified is false)
        {
            // A failing audit write must not change the answer: the refusal is the safe action
            // and the client needs the stable code. The failure of the audit write is logged
            // instead, exactly as the delivery audit of the invitation flow does it.
            try
            {
                await _audit.RecordAsync(
                    user.Id,
                    UserSecurityEventTypes.LoginBlockedUnverified,
                    null,
                    _currentUser.Ip,
                    "email_not_verified",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "The security audit event {EventType} for account {UserId} could not be written.",
                    UserSecurityEventTypes.LoginBlockedUnverified,
                    user.Id);
            }

            throw new EmailNotVerifiedException(
                "The email address of this account has not been confirmed yet.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(GetExpiryMinutes());
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role.Name),
            new Claim(ClaimTypes.Email, user.Email)
        };
        var credentials = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            ToSummary(user, user.Role.Name));
    }

    public async Task<AuthUserSummary?> GetCurrentUserAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        return user is null ? null : ToSummary(user, user.Role.Name);
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BusinessRuleException("JWT signing key is missing.");
        }

        return new SymmetricSecurityKey(Convert.FromBase64String(key));
    }

    private int GetExpiryMinutes() =>
        _configuration.GetValue<int?>("Jwt:ExpiryMinutes") ?? 120;

    private static AuthUserSummary ToSummary(User user, string role) =>
        new(user.Id, user.FullName, role, user.IsApproved);
}

public sealed class DuplicateRegistrationException : BusinessRuleException
{
    public DuplicateRegistrationException()
        : base("Email or phone is already registered")
    {
    }
}
