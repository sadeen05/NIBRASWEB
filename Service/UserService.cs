using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

public class UserService : IUserService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<UserService> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailVerificationService _emailVerification;

    public UserService(
        NebrasdbContext context,
        ILogger<UserService> logger,
        ICurrentUser currentUser,
        IEmailVerificationService emailVerification)
    {
        _context = context;
        _logger = logger;
        _currentUser = currentUser;
        _emailVerification = emailVerification;
    }

    public async Task<UserListResponse> GetAllAsync(
        int pageNumber,
        int pageSize,
        string? role,
        bool? isApproved)
    {
        _logger.LogInformation("Get all users");

        if (pageNumber < 1 || pageSize < 1 || pageSize > 50)
            throw new ValidationFailedException("Page number must be at least 1 and page size must be between 1 and 50.");

        var roleName = ParseRole(role);
        var query = _context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .Where(u => roleName == null || u.Role.Name == roleName)
            .Where(u => isApproved == null || u.IsApproved == isApproved.Value);

        var totalCount = await query.CountAsync();
        var users = await query
            .Include(u => u.Role)
            .OrderBy(u => u.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new UserListResponse(
            users.Select(ToDto).ToList(),
            pageNumber,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize));

    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            _logger.LogWarning("User not found");
            return null;
        }
        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        EnsureCallerCanCreateRole(request.RoleId);

        var emailExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.Email);
        var phoneExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Phone == request.Phone);
        if (emailExists || phoneExists)
            throw new DuplicateRegistrationException();

        if (request.RoleId is < 1 or > 3)
            throw new ValidationFailedException("User role is invalid.");

        if (request.RoleId == 2 &&
            (string.IsNullOrWhiteSpace(request.CompanyName) ||
             string.IsNullOrWhiteSpace(request.CompanyRegistrationNumber)))
        {
            throw new ValidationFailedException("Investor company details are required.");
        }

        PasswordPolicy.Validate(request.Password);

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            RoleId = request.RoleId,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            IsDeleted = false,
            IsApproved = request.RoleId is 1 or 3,
            CompanyName = request.CompanyName,
            CompanyRegistrationNumber = request.CompanyRegistrationNumber
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        await _context.Entry(user).Reference(u => u.Role).LoadAsync();

        return ToDto(user);
    }

    public async Task<bool> ApproveAsync(int id, int approvedById)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            return false;
        }

        EnsureCallerCanManage(user);

        if (user.Role.Name != RoleNames.Investor ||
            string.IsNullOrWhiteSpace(user.CompanyName) ||
            string.IsNullOrWhiteSpace(user.CompanyRegistrationNumber))
        {
            throw new ValidationFailedException("Only an Investor with company details can be approved.");
        }

        user.IsApproved = true;
        user.ApprovedById = approvedById;
        user.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            _logger.LogWarning("User not found for update");
            return false;
        }

        EnsureCallerCanManage(user);

        var emailChanged = !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged)
        {
            // The address of a verified account is proven, so it may only be replaced by a
            // successful verification of the new address and never by a direct write. A legacy
            // account (EMAIL_VERIFIED IS NULL) counts as verified.
            if (user.EmailVerified is not false)
            {
                throw new BusinessRuleException(
                    "The address of a verified account cannot be changed directly. It can only be changed after the owner verifies the new address.");
            }

            // The address must be free - as the active address and as the pending address of
            // another account - and also for a soft-deleted account, which still owns its
            // address because the unique indexes cover every row.
            var addressInUse = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id != id && (u.Email == request.Email || u.PendingEmail == request.Email));
            if (addressInUse)
            {
                throw new BusinessRuleException("Email already in use by another user.");
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // The account row is locked before the verification tokens are touched, the same
            // order the token service uses, so the two paths cannot deadlock against each other.
            await _emailVerification.LockAccountAsync(id);

            user.FullName = request.FullName;
            user.Phone = request.Phone;

            if (emailChanged)
            {
                // A correction of an unverified account only changes which address has to be
                // proven: the account stays unverified, an outstanding address change of the
                // owner is dropped, and every live link of the account is retired. A link that
                // was sent to the previous address - or for a pending address - must not be able
                // to confirm anything after the correction.
                user.Email = request.Email;
                user.EmailVerified = false;
                user.EmailVerifiedAt = null;
                user.PendingEmail = null;

                await _emailVerification.InvalidateLiveTokensAsync(
                    user.Id,
                    null,
                    "admin_correction");
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The unique index refused the address; the caller gets the same controlled conflict
            // as the check above instead of a server error.
            await transaction.RollbackAsync();
            throw new BusinessRuleException("Email already in use by another user.");
        }

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            _logger.LogWarning("User not found or already deleted");
            return false;
        }

        EnsureCallerCanManage(user);

        var hasLands = await _context.Lands.AnyAsync(l => l.LandlordId == id && !l.IsDeleted);
        if (hasLands)
            throw new BusinessRuleException("Cannot delete user who owns lands.");

        var hasContracts = await _context.Contracts.AnyAsync(c => (c.InvestorId == id || c.LandlordId == id));
        if (hasContracts)
            throw new BusinessRuleException("Cannot delete user who has contracts.");

        var hasPendingCancellations = await _context.Contracts.AnyAsync(c =>
            (c.LandlordId == id || c.InvestorId == id)
            && c.CancellationRequestedById != null
            && (c.CancellationEffectiveDate >= DateTime.UtcNow || c.DisputeFlagged));
        if (hasPendingCancellations)
            throw new BusinessRuleException("Cannot delete user with pending contract cancellations.");

        user.IsDeleted = true;

        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return true;
    }

    private static UserDto ToDto(User user) =>
        new(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.Role?.Name ?? string.Empty,
            user.CreatedAt,
            user.IsApproved,
            user.CompanyName,
            user.CompanyRegistrationNumber);

    private static string? ParseRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return role switch
        {
            RoleNames.Landlord => RoleNames.Landlord,
            RoleNames.Investor => RoleNames.Investor,
            RoleNames.Admin => RoleNames.Admin,
            RoleNames.SuperAdmin => RoleNames.SuperAdmin,
            _ => throw new ValidationFailedException("Role filter is invalid.")
        };
    }

    /// <summary>
    /// Whether the failure is a unique-index violation. The unique index on the address covers
    /// every row, including the soft-deleted ones, so a conflict is answered as a conflict
    /// instead of surfacing as a server error.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private void EnsureCallerCanCreateRole(int roleId)
    {
        if (_currentUser.Role is not (RoleNames.Admin or RoleNames.SuperAdmin))
            throw new ForbiddenException("You are not authorized to create users.");

        if (roleId is < 1 or > 3 ||
            (_currentUser.Role == RoleNames.Admin && roleId != 1 && roleId != 2))
        {
            throw new ForbiddenException("You are not authorized to create this user role.");
        }
    }

    private void EnsureCallerCanManage(User target)
    {
        if (_currentUser.Id == target.Id)
            throw new ForbiddenException("You cannot manage your own account.");

        if (target.Role.Name == RoleNames.SuperAdmin)
            throw new ForbiddenException("SuperAdmin accounts cannot be managed through the API.");

        if (_currentUser.Role == RoleNames.Admin &&
            target.Role.Name is not (RoleNames.Landlord or RoleNames.Investor))
        {
            throw new ForbiddenException("Admins may manage only Landlord and Investor accounts.");
        }

        if (_currentUser.Role != RoleNames.Admin && _currentUser.Role != RoleNames.SuperAdmin)
            throw new ForbiddenException("You are not authorized to manage users.");
    }
}