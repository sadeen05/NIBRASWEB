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

        // The list shows the latest approver of each investor. The actors are loaded in one
        // query instead of one query per row.
        var actorIds = users
            .Where(u => u.ApprovedById.HasValue)
            .Select(u => u.ApprovedById!.Value)
            .Distinct()
            .ToList();
        var actors = actorIds.Count == 0
            ? new Dictionary<int, User>()
            : await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

        User? ApprovedByFor(User user) =>
            user.ApprovedById is int actorId && actors.TryGetValue(actorId, out var actor)
                ? actor
                : null;

        return new UserListResponse(
            users.Select(user => ToDto(user, ApprovedByFor(user))).ToList(),
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

        var approvedBy = await LoadApproverAsync(user.ApprovedById);
        return ToDto(user, approvedBy);
    }

    public async Task<UserApprovalStatusDto?> GetApprovalStatusAsync(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            return null;
        }

        var approvedBy = await LoadApproverAsync(user.ApprovedById);
        var history = await _context.UserSecurityEvents
            .AsNoTracking()
            .Where(e => e.UserId == id && (e.EventType == UserSecurityEventTypes.AccountApproved || e.EventType == UserSecurityEventTypes.AccountApprovalRejected))
            .Include(e => e.Actor)
                .ThenInclude(a => a!.Role)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new UserApprovalHistoryEntryDto(
                e.Id,
                e.ActorId,
                e.ActorNameSnapshot ?? (e.Actor != null ? e.Actor.FullName : null),
                e.ActorRoleSnapshot ?? (e.Actor != null && e.Actor.Role != null ? e.Actor.Role.Name : null),
                e.CreatedAt,
                e.Details, e.EventType,
                e.ActorNameSnapshot != null && e.ActorRoleSnapshot != null))
            .ToListAsync();

        var isInvestor = user.Role.Name == RoleNames.Investor;
        var companyDetailsPresent =
            !string.IsNullOrWhiteSpace(user.CompanyName) &&
            !string.IsNullOrWhiteSpace(user.CompanyRegistrationNumber);

        return new UserApprovalStatusDto(
            user.Id,
            user.Role.Name,
            isInvestor,
            user.EmailVerified,
            companyDetailsPresent,
            user.IsApproved,
            !user.IsApproved && isInvestor && user.EmailVerified is not false && companyDetailsPresent,
            user.ApprovedById,
            user.ApprovedByNameSnapshot ?? approvedBy?.FullName,
            user.ApprovedByRoleSnapshot ?? approvedBy?.Role?.Name,
            user.ApprovedAt,
            history,
            user.ApprovedByNameSnapshot != null && user.ApprovedByRoleSnapshot != null);
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
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            // Two creations of the same address or phone number at the same moment: the unique
            // index refused the second one. The answer is the same conflict the pre-checks
            // above give, and the failed save leaves neither an account nor an audit event
            // behind. The message repeats neither the address nor the phone number.
            _context.ChangeTracker.Clear();
            throw new DuplicateRegistrationException();
        }
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

    public Task<bool> ApproveAsync(int id, int approvedById)
    {
        if (_currentUser.Id != approvedById)
            throw new ForbiddenException("The approval actor must be the authenticated administrator.");
        return DecideApprovalAsync(id, true, null);
    }

    public Task<bool> RejectApprovalAsync(int id, RejectInvestorApprovalRequest request) =>
        DecideApprovalAsync(id, false, request.Reason);

    private async Task<bool> DecideApprovalAsync(int id, bool approved, string? reason)
    {
        if (_currentUser.Id is not int actorId ||
            _currentUser.Role is not (RoleNames.Admin or RoleNames.SuperAdmin))
            throw new ForbiddenException("Only administrators may decide investor approval.");
        if (!approved && (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 ||
            reason.Any(c => char.IsControl(c) && c is not ('\t' or '\n'))))
            throw new ValidationFailedException("A rejection reason of at most 500 characters is required.");
        await using var tx = await _context.Database.BeginTransactionAsync();
        if (!await _emailVerification.LockAccountAsync(id)) return false;
        var user = await _context.Users.Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user is null) return false;
        await _context.Entry(user).ReloadAsync();
        await _context.Entry(user).Reference(u => u.Role).Query().LoadAsync();
        await _context.Entry(user.Role).ReloadAsync();
        if (user.IsDeleted) return false;
        EnsureCallerCanManage(user);
        var actor = await _context.Users.FromSqlInterpolated($"SELECT * FROM [USERS] WITH (UPDLOCK, HOLDLOCK) WHERE [ID] = {actorId}").AsNoTracking().Include(u => u.Role)
            .SingleOrDefaultAsync(u => !u.IsDeleted);
        if (actor?.Role.Name is not (RoleNames.Admin or RoleNames.SuperAdmin) ||
            actor.Role.Name != _currentUser.Role)
            throw new ForbiddenException("The approval actor is no longer authorized.");
        if (user.Role.Name != RoleNames.Investor)
            throw new ValidationFailedException("Only Investor accounts have investor approval decisions.");
        if (approved && (string.IsNullOrWhiteSpace(user.CompanyName) ||
            string.IsNullOrWhiteSpace(user.CompanyRegistrationNumber)))
            throw new ValidationFailedException("Only an Investor with company details can be approved.");
        if (approved && user.EmailVerified is false)
            throw new BusinessRuleException("The email address of the investor must be verified before the account can be approved.");
        var now = DateTime.UtcNow;
        user.IsApproved = approved;
        if (approved) {
            user.ApprovedById=actorId; user.ApprovedAt=now;
            user.ApprovedByNameSnapshot=actor.FullName; user.ApprovedByRoleSnapshot=actor.Role.Name;
        }
        // A rejection changes current eligibility, and preserves the last approval and every event.
        _context.UserSecurityEvents.Add(new UserSecurityEvent {
            UserId=id, EventType=approved ? UserSecurityEventTypes.AccountApproved : UserSecurityEventTypes.AccountApprovalRejected,
            ActorId=actorId, ActorNameSnapshot=actor.FullName, ActorRoleSnapshot=actor.Role.Name,
            Details=approved ? "investor_approval" : reason!.Trim(), CreatedAt=now
        });
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
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

        await using var transaction = await _context.Database.BeginTransactionAsync();
        if (!await _emailVerification.LockAccountAsync(id)) { return false; }
        await _context.Entry(user).ReloadAsync();
        if (user.IsDeleted) { return false; }
        // Query the persisted FK under the account lock without clearing a non-null navigation.
        await _context.Entry(user).Reference(u => u.Role).Query().LoadAsync();
        await _context.Entry(user.Role).ReloadAsync();
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

        // The phone number is unique across every row - active and soft-deleted - exactly like
        // the address. The pre-check gives the controlled field-specific conflict normally; the
        // unique index stays the authority for the race window, and the catch below classifies
        // what it refused. The answer never identifies the other account.
        var phoneChanged = !string.Equals(user.Phone, request.Phone, StringComparison.Ordinal);
        if (phoneChanged)
        {
            var phoneInUse = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id != id && u.Phone == request.Phone);
            if (phoneInUse)
            {
                throw new BusinessRuleException("Phone already in use by another user.");
            }
        }


        try
        {
            // The account row is locked before the verification tokens are touched, the same
            // order the token service uses, so the two paths cannot deadlock against each other.


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
            // The unique index refused the address or the phone number. The index stays the
            // authority: after the rollback a fresh read decides which of the two unique
            // indexes won, so the caller is told exactly which field to correct - without any
            // information about the other account. A value that is free again by now (it raced
            // differently, or the winner reclaimed it) keeps the previous neutral answer.
            await transaction.RollbackAsync();
            var phoneClaimed = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id != id && u.Phone == request.Phone);
            if (phoneClaimed)
                throw new BusinessRuleException("Phone already in use by another user.");
            var addressClaimed = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id != id && (u.Email == request.Email || u.PendingEmail == request.Email));
            if (addressClaimed)
                throw new BusinessRuleException("Email already in use by another user.");
            throw new BusinessRuleException("Email or phone already in use by another user.");
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

        user.IsDeleted = true;

        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<User?> LoadApproverAsync(int? approvedById)
    {
        if (approvedById is not int actorId)
        {
            return null;
        }

        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == actorId);
    }

    private static UserDto ToDto(User user, User? approvedBy = null) =>
        new(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.Role?.Name ?? string.Empty,
            user.CreatedAt,
            user.IsApproved,
            user.CompanyName,
            user.CompanyRegistrationNumber,
            user.EmailVerified,
            user.EmailVerifiedAt,
            user.PendingEmail,
            user.ApprovedById,
            user.ApprovedByNameSnapshot ?? approvedBy?.FullName,
            user.ApprovedByRoleSnapshot ?? approvedBy?.Role?.Name,
            user.ApprovedAt,
            user.ApprovedByNameSnapshot != null && user.ApprovedByRoleSnapshot != null);

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