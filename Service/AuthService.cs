using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public sealed class AuthService : IAuthService
{
    private const string DummyPasswordHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEe.6QxT5s0L9L8h1f4mM7Q7V9n2M3j7b6W";
    private readonly NebrasdbContext _context;
    private readonly IConfiguration _configuration;

    public AuthService(NebrasdbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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

        if (await _context.Users.AnyAsync(u =>
                (u.Email == request.Email || u.Phone == request.Phone)))
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

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        PasswordPolicy.Validate(request.Password);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => !u.IsDeleted && u.Email == request.Email);

        if (user is null || user.IsDeleted)
        {
            BCrypt.Net.BCrypt.Verify(request.Password, DummyPasswordHash);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return null;
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
