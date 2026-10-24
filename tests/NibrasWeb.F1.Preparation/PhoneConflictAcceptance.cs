using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NibrasWeb.F1.Preparation;

// Real-database conflict checks for the phone/email uniqueness classification (UserService
// profile update and creation, AuthService registration). They run only in the separately
// gated --sql-acceptance mode on the approved disposable target (SADEEN\SQLEXPRESS /
// NIBRAS_F1_Verify). Every fixture is prefix-owned; no history, migration or unrelated row is
// changed or deleted, and the database unique indexes stay the final authority for every conflict.
public static class PhoneConflictAcceptance
{
    private const string PhoneConflictMessage = "Phone already in use by another user.";
    private const string EmailConflictMessage = "Email already in use by another user.";

    // Registration/profile delivery is exercised offline: the mailer answers failure
    // deterministically, so no SMTP is involved and the delivered outcome (Failed, account
    // stays unverified) never depends on a mail server.
    private sealed class FailingMailer : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmailSendResult.Failure("offline-test-delivery-failure"));
    }

    private static EmailOptions OfflineOptions() => new()
    {
        ResendCooldownSeconds = 0,
        ResendDailyCap = 100,
        FrontendBaseUrl = "http://localhost",
    };

    private static EmailVerificationService Tokens(NebrasdbContext db, TestClock clock) =>
        new(db, Options.Create(OfflineOptions()), NullLogger<EmailVerificationService>.Instance, clock);

    private static RegistrationVerificationService RegistrationDelivery(NebrasdbContext db, TestClock clock) =>
        new(db, Tokens(db, clock), new FailingMailer(), new UserSecurityEventService(db, clock),
            Options.Create(OfflineOptions()), NullLogger<RegistrationVerificationService>.Instance, clock);

    // The admin-managed profile path. The delivery dependencies are unused by UpdateAsync.
    private static UserService ProfileService(NebrasdbContext db, int actorId) =>
        new(db, NullLogger<UserService>.Instance, new TestUser(actorId, RoleNames.Admin),
            Tokens(db, SqlAcceptance.Clock()), null!, null!);

    // The admin creation path; delivery runs offline through the failing mailer.
    private static UserService CreateService(NebrasdbContext db, int actorId)
    {
        var clock = SqlAcceptance.Clock();
        var tokens = Tokens(db, clock);
        var delivery = RegistrationDelivery(db, clock);
        return new UserService(db, NullLogger<UserService>.Instance, new TestUser(actorId, RoleNames.SuperAdmin),
            tokens, null!, delivery);
    }

    private static AuthService RegistrationServiceFor(NebrasdbContext db) =>
        new(db, new ConfigurationBuilder().Build(),
            new UserSecurityEventService(db, SqlAcceptance.Clock()),
            new TestUser(0, RoleNames.Landlord),
            RegistrationDelivery(db, SqlAcceptance.Clock()),
            NullLogger<AuthService>.Instance);

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static string NextPhone() => "9" + Random.Shared.NextInt64(100000000, 999999999);

    private static async Task<string> StoredPhoneAsync(int userId)
    {
        await using var db = SqlAcceptance.Context();
        return (await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Phone).SingleAsync())!;
    }

    public static async Task RunAsync()
    {
        await ProfilePhonePreCheckAsync();
        await SoftDeletedPhoneStaysReservedAsync();
        await EmailConflictClassificationPreservedAsync();
        await ConcurrentPhoneClaimRaceAsync();
        await ConcurrentCreatePhoneRaceAsync();
        await ConcurrentSelfRegistrationPhoneRaceAsync();
    }

    // A controlled, field-specific refusal before anything is written: the target's stored
    // phone and the other account are untouched, and no account information is revealed.
    private static async Task ProfilePhonePreCheckAsync()
    {
        await using var db = SqlAcceptance.Context();
        var target = await SqlAcceptance.FixtureAsync(db, true);
        var other = await SqlAcceptance.FixtureAsync(db, true);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.Admin);

        var refused = await CaptureAsync(async () =>
        {
            var service = ProfileService(db, actor.Id);
            await service.UpdateAsync(target.Id, new UpdateUserRequest(target.FullName, target.Email, other.Phone!));
        });

        Program.Check(refused is BusinessRuleException { Message: PhoneConflictMessage },
            "profile change to another account's phone is a controlled phone-specific conflict");
        var stored = await StoredPhoneAsync(target.Id);
        var otherPhone = await StoredPhoneAsync(other.Id);
        Program.Check(stored == target.Phone && otherPhone == other.Phone,
            "the refused update changed neither account's stored phone");
    }

    // The unique indexes cover soft-deleted rows too, so a retired account's number stays
    // reserved and the conflict is the same phone-specific one.
    private static async Task SoftDeletedPhoneStaysReservedAsync()
    {
        await using var db = SqlAcceptance.Context();
        var target = await SqlAcceptance.FixtureAsync(db, true);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.Admin);
        var reserved = NextPhone();
        var deleted = new User
        {
            RoleId = await db.Roles.Where(r => r.Name == RoleNames.Landlord).Select(r => r.Id).SingleAsync(),
            FullName = "f1host-deleted-phone",
            Email = "f1host-deleted-phone-" + Guid.NewGuid().ToString("N") + "@example.test",
            Phone = reserved,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("F1!deleted-phone-1", workFactor: 12),
            IsDeleted = true,
        };
        db.Users.Add(deleted);
        await db.SaveChangesAsync();

        var refused = await CaptureAsync(async () =>
        {
            var service = ProfileService(db, actor.Id);
            await service.UpdateAsync(target.Id, new UpdateUserRequest(target.FullName, target.Email, reserved));
        });

        Program.Check(refused is BusinessRuleException { Message: PhoneConflictMessage },
            "a soft-deleted account's phone stays reserved with a phone-specific conflict");
        Program.Check(await StoredPhoneAsync(target.Id) == target.Phone,
            "the target's stored phone is unchanged by the refused update");
    }

    // The address classification keeps its existing form on the same path (this documents the
    // change is additive, not a rewrite): an unverified account's address change to another
    // active account's address answers the specific address conflict.
    private static async Task EmailConflictClassificationPreservedAsync()
    {
        await using var db = SqlAcceptance.Context();
        var target = await SqlAcceptance.FixtureAsync(db, false);
        var other = await SqlAcceptance.FixtureAsync(db, true);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.Admin);

        var refused = await CaptureAsync(async () =>
        {
            var service = ProfileService(db, actor.Id);
            await service.UpdateAsync(target.Id, new UpdateUserRequest(target.FullName, other.Email, target.Phone));
        });

        Program.Check(refused is BusinessRuleException { Message: EmailConflictMessage },
            "an address change to another account's address keeps the specific conflict");
    }

    // The race: two different accounts are moved to the same phone number at the same moment.
    // The unique index decides - exactly one claimant wins, the loser receives the
    // phone-specific conflict after its rollback, and the number ends owned by exactly one account.
    private static async Task ConcurrentPhoneClaimRaceAsync()
    {
        await using var setup = SqlAcceptance.Context();
        var first = await SqlAcceptance.FixtureAsync(setup, true);
        var second = await SqlAcceptance.FixtureAsync(setup, true);
        var actor = await SqlAcceptance.FixtureAsync(setup, true, RoleNames.Admin);
        var sharedPhone = NextPhone();

        var pair = await Task.WhenAll(
            UpdateProfileAsync(actor.Id, first.Id, sharedPhone),
            UpdateProfileAsync(actor.Id, second.Id, sharedPhone));

        Program.Check(pair.Count(r => r is null) == 1 && pair.Count(r => r is BusinessRuleException) == 1,
            "two concurrent phone claims have exactly one winner and one controlled conflict");
        Program.Check(pair.All(r => r is null || r!.Message == PhoneConflictMessage),
            "the database-conflict classification answers the phone-specific message");

        await using var verify = SqlAcceptance.Context();
        var owners = await verify.Users.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(u => u.Phone == sharedPhone);
        Program.Check(owners == 1, "exactly one account owns the claimed phone number");
    }

    private static async Task<Exception?> UpdateProfileAsync(int actorId, int userId, string phone)
    {
        try
        {
            await using var db = SqlAcceptance.Context();
            var service = ProfileService(db, actorId);
            var identity = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FullName, u.Email })
                .SingleAsync();
            await service.UpdateAsync(userId, new UpdateUserRequest(identity.FullName, identity.Email, phone));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // The same race for admin-created accounts: two creations of the same identity at once.
    // Before this mission the loser surfaced as a server error (500); now the unique index's
    // refusal is the same conflict the pre-check gives.
    private static async Task ConcurrentCreatePhoneRaceAsync()
    {
        await using var setup = SqlAcceptance.Context();
        var actor = await SqlAcceptance.FixtureAsync(setup, true, RoleNames.SuperAdmin);
        var email = "f1host-create-race-" + Guid.NewGuid().ToString("N") + "@example.test";
        var phone = NextPhone();
        var password = "F1race-Password-2026!";

        var pair = await Task.WhenAll(
            CreateUserAsync(actor.Id, email + ".a", phone, password),
            CreateUserAsync(actor.Id, email + ".b", phone, password));

        Program.Check(pair.Count(r => r is null) == 1 && pair.Count(r => r is DuplicateRegistrationException) == 1,
            "two concurrent creations of the same identity have one winner and the merged conflict");
        Program.Check(pair.All(r => r is null || r!.Message == "Email or phone is already registered"),
            "the concurrent-creation conflict message repeats neither the address nor the phone number");

        await using var verify = SqlAcceptance.Context();
        var accounts = await verify.Users.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(u => u.Phone == phone);
        Program.Check(accounts == 1, "exactly one account exists for the raced identity");
    }

    private static async Task<Exception?> CreateUserAsync(int actorId, string email, string phone, string password)
    {
        try
        {
            await using var db = SqlAcceptance.Context();
            var service = CreateService(db, actorId);
            await service.CreateAsync(new CreateUserRequest(
                "f1host-create-race", email, phone, 1, Password: password));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // Self-registration stays merged by design (anti-enumeration): one answer whether the
    // address or the phone collided, no reveal of either value. The unique index stays the
    // authority for two registrations racing the same number.
    private static async Task ConcurrentSelfRegistrationPhoneRaceAsync()
    {
        await using var setup = SqlAcceptance.Context();
        var phone = NextPhone();
        var password = "F1race-Password-2026!";
        var baseEmail = "f1host-register-race-" + Guid.NewGuid().ToString("N") + "@example.test";

        var pair = await Task.WhenAll(
            RegisterAsync(baseEmail + ".a", phone, password),
            RegisterAsync(baseEmail + ".b", phone, password));

        Program.Check(pair.Count(r => r is null) == 1 && pair.Count(r => r is DuplicateRegistrationException) == 1,
            "two concurrent registrations of the same phone have one account and the merged conflict");
        Program.Check(pair.All(r => r is null || r!.Message == "Email or phone is already registered"),
            "the registration conflict message stays merged and neutral");

        await using var verify = SqlAcceptance.Context();
        var accounts = await verify.Users.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(u => u.Phone == phone);
        Program.Check(accounts == 1, "exactly one registration survives the race");
    }

    private static async Task<Exception?> RegisterAsync(string email, string phone, string password)
    {
        try
        {
            await using var db = SqlAcceptance.Context();
            var service = RegistrationServiceFor(db);
            await service.RegisterAsync(new RegisterRequest(
                "f1host-register-race", email, phone, password, 1, null, null));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
