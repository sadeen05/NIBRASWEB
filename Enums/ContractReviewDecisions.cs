namespace NibrasWeb.Enums
{
    /// <summary>
    /// Append-only decision values stored in <c>CONTRACT_REVIEWS.DECISION</c> (max length 20).
    /// Values match the strings used by the original contract lifecycle so existing rows stay valid.
    /// </summary>
    public static class ContractReviewDecisions
    {
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Terminated = "Terminated";

        // Contract-revision decisions. Each value is at most 20 characters, matching DECISION.
        public const string RevisionApproved = "RevisionApproved";
        public const string RevisionEscalated = "RevisionEscalated";
        public const string RevisionContinued = "RevisionContinued";
        public const string RevisionCancelled = "RevisionCancelled";
    }
}
