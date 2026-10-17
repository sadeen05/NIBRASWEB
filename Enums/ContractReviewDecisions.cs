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
    }
}
