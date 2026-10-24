namespace NibrasWeb.Enums
{
    public enum ContractStatus
    {
        PendingSignatures = 1,
        Active = 2,
        Terminated = 3,
        Rejected = 4,

        /// <summary>
        /// A pre-signing contract cancelled by a SuperAdmin while escalated from the
        /// contract-revision workflow. Terminal; the linked reservation is released and
        /// the land returns to Verified using the same established path as rejection.
        /// </summary>
        Cancelled = 5,
    }
}
