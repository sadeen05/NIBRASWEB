using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Real-database delivery checks for the admin activation invitation. They run only in the
// separately gated --sql-acceptance mode and use the approved disposable target
// (SADEEN\SQLEXPRESS / NIBRAS_F1_Verify). No external SMTP is contacted: the mailer is an
// in-process fake, and every fixture is prefix-owned.
public static class AdminInvitationAcceptance
{
    private sealed class InstantMailer : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(EmailSendResult.Success);
    }

    private sealed class FailingMailer : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(EmailSendResult.Failure("smtp_delivery_failed"));
    }

    private static EmailOptions Settings() => new()
    {
        ResendCooldownSeconds = 0,
        ResendDailyCap = 100,
        FrontendBaseUrl = "http://localhost",
        ActivationPath = "/activate-admin",
    };

    private static AdminInvitationService Service(NebrasdbContext db, TestClock clock, IEmailSender mailer)
    {
        var options = Options.Create(Settings());
        return new AdminInvitationService(
            db,
            new EmailVerificationService(db, options, NullLogger<EmailVerificationService>.Instance, clock),
            mailer,
            new UserSecurityEventService(db, clock),
            options,
            NullLogger<AdminInvitationService>.Instance,
            clock);
    }

    private static Task<int> LiveLinksAsync(NebrasdbContext db, int userId) =>
        db.EmailVerificationTokens.CountAsync(t =>
            t.UserId == userId && t.Purpose == EmailVerificationPurposes.AdminActivation && t.ConsumedAt == null);

    private static Task<bool> HasEventAsync(NebrasdbContext db, int userId, string eventType) =>
        db.UserSecurityEvents.AnyAsync(e => e.UserId == userId && e.EventType == eventType);

    public static async Task RunAsync()
    {
        await SuccessAsync();
        await FailedDeliveryAsync();
        await TimeoutAsync();
        await CallerCancellationAsync();
    }

    private static async Task SuccessAsync()
    {
        await using var db = SqlAcceptance.Context();
        var clock = SqlAcceptance.Clock();
        var admin = await SqlAcceptance.FixtureAsync(db, false, RoleNames.Admin);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.SuperAdmin);
        var service = Service(db, clock, new InstantMailer());

        var result = await service.SendAsync(admin.Id, actor.Id);

        Program.Check(result.Sent, "admin invitation success is reported as Sent");
        Program.Check(await LiveLinksAsync(db, admin.Id) == 1,
            "successful admin invitation leaves exactly one usable link");
        Program.Check(await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailSent),
            "successful admin invitation records the sent audit event");
        Program.Check(!await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailDeliveryFailed),
            "successful admin invitation records no delivery-failed event");
    }

    private static async Task FailedDeliveryAsync()
    {
        await using var db = SqlAcceptance.Context();
        var clock = SqlAcceptance.Clock();
        var admin = await SqlAcceptance.FixtureAsync(db, false, RoleNames.Admin);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.SuperAdmin);
        var service = Service(db, clock, new FailingMailer());

        var result = await service.SendAsync(admin.Id, actor.Id);

        Program.Check(result.Outcome == AdminInvitationOutcome.DeliveryFailed,
            "admin invitation transport failure is reported as DeliveryFailed");
        Program.Check(await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailDeliveryFailed) &&
            !await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailSent),
            "failed admin invitation records only the delivery-failed event");
    }

    private static async Task TimeoutAsync()
    {
        await using var db = SqlAcceptance.Context();
        var clock = SqlAcceptance.Clock();
        var admin = await SqlAcceptance.FixtureAsync(db, false, RoleNames.Admin);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.SuperAdmin);
        var service = Service(db, clock, new CancellableMailer());

        var result = await service.SendAsync(admin.Id, actor.Id).WaitAsync(TimeSpan.FromSeconds(20));

        Program.Check(result.Outcome == AdminInvitationOutcome.DeliveryFailed &&
            result.FailureReason == "delivery_timeout",
            "stalled admin invitation is bounded and classified as delivery_timeout");
        Program.Check(await LiveLinksAsync(db, admin.Id) == 0,
            "timed-out admin invitation leaves no usable link");
        Program.Check(await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailDeliveryFailed) &&
            !await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailSent),
            "timed-out admin invitation records no sent event");
    }

    private static async Task CallerCancellationAsync()
    {
        await using var db = SqlAcceptance.Context();
        var clock = SqlAcceptance.Clock();
        var admin = await SqlAcceptance.FixtureAsync(db, false, RoleNames.Admin);
        var actor = await SqlAcceptance.FixtureAsync(db, true, RoleNames.SuperAdmin);
        var mailer = new CancellableMailer();
        var service = Service(db, clock, mailer);
        using var caller = new CancellationTokenSource();

        var send = service.SendAsync(admin.Id, actor.Id, null, caller.Token);
        await mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();

        var cancelled = false;
        try { await send; }
        catch (OperationCanceledException) { cancelled = true; }

        Program.Check(cancelled, "caller cancellation propagates for an admin invitation");
        Program.Check(await LiveLinksAsync(db, admin.Id) == 0,
            "cancelled admin invitation retires the exact link");
        Program.Check(!await HasEventAsync(db, admin.Id, UserSecurityEventTypes.VerificationEmailSent),
            "cancelled admin invitation records no sent event");
    }
}
