namespace NIBRAS.API.DTOs;

public record UserDto
(
    int Id,
    string FullName,
    string Email,
    string Phone,
    string Role,
    DateTime CreatedAt,
    bool IsApproved,
    string? CompanyName,
    string? CompanyRegistrationNumber,
    bool? EmailVerified = null,
    DateTime? EmailVerifiedAt = null,
    string? PendingEmail = null,
    int? ApprovedById = null,
    string? ApprovedByName = null,
    string? ApprovedByRole = null,
    DateTime? ApprovedAt = null,
    bool ApprovedActorSnapshotRecorded = false
);

/// <summary>One durable approval decision from the append-only security event trail.</summary>
public record UserApprovalHistoryEntryDto(
    int EventId,
    int? ActorId,
    string? ActorName,
    string? ActorRole,
    DateTime ApprovedAt,
    string? Details,
    string EventType = "AccountApproved",
    bool ActorSnapshotRecorded = false);

/// <summary>
/// The investor-approval eligibility and the complete approval history. It is separate from the
/// list DTO so the queue stays cheap and the detail view can show exactly which administrator
/// approved the account, when, and under which role.
/// </summary>
public record UserApprovalStatusDto(
    int UserId,
    string Role,
    bool IsInvestor,
    bool? EmailVerified,
    bool CompanyDetailsPresent,
    bool IsApproved,
    bool EligibleForApproval,
    int? ApprovedById,
    string? ApprovedByName,
    string? ApprovedByRole,
    DateTime? ApprovedAt,
    IReadOnlyList<UserApprovalHistoryEntryDto> History,
    bool ApprovedActorSnapshotRecorded = false);

public record UserListResponse(
    IReadOnlyList<UserDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// The service-level answer to an account creation: the account and what happened to the
/// activation invitation of a new administrator account. The API response keeps the flat
/// user shape and adds the invitation status, so every existing consumer of
/// <c>POST /api/users</c> keeps working.
/// </summary>
/// <param name="User">The created account.</param>
/// <param name="Invitation">
/// <c>not_applicable</c> (the account is not a new administrator account), <c>handed_over</c>
/// (the invitation was handed to the mailer) or <c>failed</c> (it could not be handed over; the
/// account stays inactive and the invitation can be sent again). It reports what happened at the
/// mailer - it never claims that a message was received.
/// </param>
/// <param name="InvitationMessage">A short explanation when the invitation needs attention.</param>
public record UserCreationResponse(
    UserDto User,
    string Invitation,
    string? InvitationMessage = null,
    string? Verification = null,
    string? VerificationMessage = null);
