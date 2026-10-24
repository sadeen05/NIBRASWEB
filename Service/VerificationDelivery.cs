namespace NIBRAS.API.Services;

// Cancellation preserves the active identity and audit trail. Cleanup uses a separate bounded
// token so client disconnection cannot leave the exact interrupted token redeemable.
public static class VerificationDelivery
{
    public static async Task<EmailSendResult> SendAsync(IEmailVerificationService verification,
        IEmailSender sender, int userId, string purpose, IssuedVerificationToken issued,
        EmailMessage message, CancellationToken cancellationToken)
    {
        try { return await sender.SendAsync(message, cancellationToken); }
        catch (OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await verification.CancelDeliveryAsync(userId, issued.Plaintext, purpose, cleanup.Token);
            throw;
        }
    }
}