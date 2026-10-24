using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Deterministic, offline checks for the narrow cancellation boundary that cannot be reproduced
// with a real SQL Server without an in-doubt commit. They model the two orderings around
// ResendTokenAsync: a commit that is observed as cancelled, and a genuine caller cancellation.
public static class AdminInvitationCancellationMocks
{
    public class InvitationProbe : DispatchProxy
    {
        public Func<Task<EmailVerificationResult>>? Resend;
        public Action? Cancel;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            return method!.Name switch
            {
                nameof(IEmailVerificationService.ResendTokenAsync) => Resend!(),
                nameof(IEmailVerificationService.CancelDeliveryAsync) => Cancel is null
                    ? Task.CompletedTask
                    : Wrap(),
                _ => throw new InvalidOperationException("Unexpected dependency."),
            };

            Task Wrap()
            {
                Cancel!();
                return Task.CompletedTask;
            }
        }
    }

    private sealed class RecordingAudit : IUserSecurityEventService
    {
        public List<string> Events { get; } = [];

        public Task RecordAsync(
            int userId,
            string eventType,
            int? actorId = null,
            string? ip = null,
            string? details = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(eventType);
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedMailer : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The mailer must not be reached in this boundary test.");
    }

    private static AdminInvitationService Service(
        IEmailVerificationService verification, IUserSecurityEventService audit)
    {
        var db = new OfflineDatabase();
        db.Users.Add(new User
        {
            Id = 42,
            Email = "admin@example.test",
            FullName = "Invitation Fixture",
            Phone = "900000042",
            PasswordHash = "not-used",
            RoleId = 3,
        });
        var options = Options.Create(new EmailOptions
        {
            FrontendBaseUrl = "https://example.test",
            ActivationPath = "/activate-admin",
        });
        return new AdminInvitationService(
            db.Context(),
            verification,
            new UnusedMailer(),
            audit,
            options,
            NullLogger<AdminInvitationService>.Instance,
            new TestClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    public static async Task RunAsync()
    {
        await InDoubtCommitAsync();
        await CallerCancellationAsync();
    }

    private static async Task InDoubtCommitAsync()
    {
        var probe = DispatchProxy.Create<IEmailVerificationService, InvitationProbe>();
        var verification = (InvitationProbe)(object)probe;
        var audit = new RecordingAudit();
        var committed = false;
        verification.Resend = () =>
        {
            // The token row is committed, but the connection observes cancellation before the
            // method can return. This is the exact in-doubt boundary.
            committed = true;
            return Task.FromException<EmailVerificationResult>(new OperationCanceledException());
        };

        var result = await Service(probe, audit).SendAsync(42, 1, "127.0.0.1");

        Program.Check(committed && result.Outcome == AdminInvitationOutcome.DeliveryFailed &&
            result.FailureReason == "delivery_timeout",
            "in-doubt commit is bounded and reported as delivery_timeout");
        Program.Check(audit.Events.Contains(UserSecurityEventTypes.VerificationEmailDeliveryFailed) &&
            !audit.Events.Contains(UserSecurityEventTypes.VerificationEmailSent),
            "in-doubt commit records no contradictory sent event");
        Program.Check(!result.ToString()!.Contains("admin@example.test", StringComparison.OrdinalIgnoreCase),
            "in-doubt result exposes neither recipient nor token");
    }

    private static async Task CallerCancellationAsync()
    {
        var probe = DispatchProxy.Create<IEmailVerificationService, InvitationProbe>();
        var verification = (InvitationProbe)(object)probe;
        var audit = new RecordingAudit();
        var cleanupCalled = false;
        verification.Resend = () =>
            Task.FromException<EmailVerificationResult>(new OperationCanceledException());
        verification.Cancel = () => cleanupCalled = true;

        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var propagated = false;
        try
        {
            await Service(probe, audit).SendAsync(42, 1, null, caller.Token);
        }
        catch (OperationCanceledException)
        {
            propagated = true;
        }

        Program.Check(propagated, "genuine caller cancellation still propagates");
        Program.Check(!audit.Events.Contains(UserSecurityEventTypes.VerificationEmailSent),
            "genuine caller cancellation records no sent event");
        Program.Check(!cleanupCalled,
            "issuance that observed caller cancellation leaves no issued token to retire");
    }
}
