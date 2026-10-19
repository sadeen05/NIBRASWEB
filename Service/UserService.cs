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
    private readonly IAdminInvitationService _adminInvitation;
    private readonly IRegistrationVerificationService _registrationVerification;

    public UserService(
        NebrasdbContext context,
        ILogger<UserService> logger,
        ICurrentUser currentUser,
        IEmailVerificationService emailVerification,
        IAdminInvitationService adminInvitation,
        IRegistrationVerificationService registrationVerification)
    {
        _context = context;
        _logger = logger;
        _currentUser = currentUser;
        _emailVerification = emailVerification;
        _adminInvitation = adminInvitation;
        _registrationVerification = registrationVerification;
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

    public async Task<UserCreationResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
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

        // A Landlord or an Investor chooses their own password at creation. A new Admin does
        // not: the super administrator who creates the account must never know or choose the
        // final password, so the account starts with a hash nobody can match and the new Admin
        // sets their own password through the single-use activation link.
        var isAdminAccount = request.RoleId == 3;
        if (!isAdminAccount)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ValidationFailedException("A password is required for a landlord or an investor account.");
            }

            if (request.Password.Length < 10 || request.Password.Length > 128)
            {
                throw new ValidationFailedException("The password must be between 10 and 128 characters.");
            }

            PasswordPolicy.Validate(request.Password);
        }

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            RoleId = request.RoleId,
            PasswordHash = isAdminAccount
                ? CreateUnusablePasswordHash()
                : BCrypt.Net.BCrypt.HashPassword(request.Password!, workFactor: 12),
            IsDeleted = false,
            IsApproved = request.RoleId is 1 or 3,
            // Every new account has to prove its address before it may be used: a new Admin
            // through its own activation link, a new landlord or investor through a
            // verification mail. A legacy account (NULL) counts as verified and is never
            // touched by this path.
            EmailVerified = false,
            EmailVerifiedAt = null,
            CompanyName = request.CompanyName,
            CompanyRegistrationNumber = request.CompanyRegistrationNumber
        };

        // The account and its audit event are written in one transaction: the trail can never
        // disagree with the accounts. The event is linked through the navigation, so the
        // foreign key is filled in when the account is inserted.
        _context.Users.Add(user);
        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            User = user,
            EventType = isAdminAccount
                ? UserSecurityEventTypes.AdminAccountCreated
                : UserSecurityEventTypes.AdminCreatedUnverified,
            ActorId = _currentUser.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
        await _context.Entry(user).Reference(u => u.Role).LoadAsync();

        var dto = ToDto(user);
        if (!isAdminAccount)
        {
            // A new landlord or investor is verified the same way a self-registered account
            // is: the account stays unverified until the address is confirmed.
            // The verification mail is delivered in process: the plaintext token never returns
            // to the caller and never reaches a log. A delivery failure leaves the account
            // unverified and unusable, and the verification can be requested again - which
            // retires the failed link, so a retry never leaves two usable links and never
            // creates a second account. The delivery is bounded, so the request is not held
            // for the whole timeout of the mail server.
            using var deliveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deliveryCts.CancelAfter(
                TimeSpan.FromSeconds(RegistrationVerificationService.OnboardingDeliveryTimeoutSeconds));

            try
            {
                var verification = await _registrationVerification.SendAsync(
                    user.Id,
                    _currentUser.Id,
                    null,
                    deliveryCts.Token);

                if (!verification.Sent)
                {
                    _logger.LogWarning(
                        "The verification mail for account {UserId} could not be handed over; it can be requested again.",
                        user.Id);

                    return new UserCreationResponse(
                        dto,
                        VerificationStatuses.NotApplicable,
                        null,
                        VerificationStatuses.Failed,
                        "The verification mail could not be handed over for delivery. The account stays unverified until the address is confirmed.");
                }
            }
            catch (Exception ex)
            {
                // The account exists and stays unverified. A failure between the creation and
                // the delivery must not create a second account and must not verify anything.
                // The exception is reported by its type only, because a database error can
                // carry the address.
                _logger.LogError(
                    "The verification mail for account {UserId} could not be prepared ({ErrorType}).",
                    user.Id,
                    ex.GetType().Name);

                return new UserCreationResponse(
                    dto,
                    VerificationStatuses.NotApplicable,
                    null,
                    VerificationStatuses.Failed,
                    "The verification mail could not be handed over for delivery. The account stays unverified until the address is confirmed.");
            }

            return new UserCreationResponse(
                dto,
                VerificationStatuses.NotApplicable,
                null,
                VerificationStatuses.HandedOver);
        }

        // The audit recorded who created the account together with the account itself.
        // The invitation is delivered in process: the plaintext token never returns to the
        // caller and never reaches a log. A delivery failure leaves the account unverified and
        // unusable, and the SuperAdmin can send the invitation again - which retires the failed
        // link, so a retry never leaves two usable invitations and never creates a second
        // account.
        var invitation = await _adminInvitation.SendAsync(user.Id, _currentUser.Id);
        if (!invitation.Sent)
        {
            _logger.LogWarning(
                "The activation invitation for admin account {UserId} could not be sent; it can be sent again.",
                user.Id);
            return new UserCreationResponse(
                dto,
                VerificationStatuses.Failed,
                "The invitation could not be handed over for delivery. The account stays inactive until the invitation succeeds.",
                VerificationStatuses.NotApplicable);
        }

        return new UserCreationResponse(
            dto,
            VerificationStatuses.HandedOver,
            null,
            VerificationStatuses.NotApplicable);
    }

    /// <summary>
    /// A password hash nobody can match: 32 random bytes are hashed and then thrown away, so
    /// the account has no usable password until the new Admin sets one through the activation
    /// link. The secret never leaves this method.
    /// </summary>
    private static string CreateUnusablePasswordHash()
    {
        var secret = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(secret);
        return BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(secret), workFactor: 12);
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
        DatabaseErrors.IsUniqueViolation(ex);

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