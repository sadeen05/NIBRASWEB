namespace NibrasWeb.Enums
{
    /// <summary>
    /// State of one pre-signing contract-revision request. Exactly one revision may be open
    /// per contract at a time (enforced by a filtered unique index on the database).
    /// </summary>
    public static class ContractRevisionStatuses
    {
        public const string PendingPartyConsent = "PendingPartyConsent";
        public const string PendingAdminReview = "PendingAdminReview";
        public const string PendingSuperAdminReview = "PendingSuperAdminReview";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static bool IsOpen(string status) =>
            status is PendingPartyConsent or PendingAdminReview or PendingSuperAdminReview;
    }

    /// <summary>Append-only decisions stored in CONTRACT_REVISION_CONSENTS.DECISION.</summary>
    public static class ContractRevisionConsentDecisions
    {
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
