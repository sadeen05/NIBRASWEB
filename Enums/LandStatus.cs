namespace NibrasWeb.Enums;

public enum LandStatus
{
    Draft = 1,
    PendingVerification = 2,
    Verified = 3,
    Rejected = 4,
    UnderContract = 5,

    /// <summary>
    /// A land that was rejected and then corrected and resubmitted by its owner.
    /// It is deliberately distinct from <see cref="PendingVerification"/>: a first
    /// submission is reviewed by a regular Admin, but a resubmission after a
    /// rejection is reviewed by a SuperAdmin (see the landlord correction flow).
    /// </summary>
    PendingSuperAdminReview = 6
}
