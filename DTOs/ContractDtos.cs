using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class CreateContractRequest
{
    [Range(1, int.MaxValue)]
    public int OfferId { get; set; }
}

public class ReviewContractRequest
{
    public bool Approve { get; set; }

    [NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string? Reason { get; set; }
}

public class TerminateContractRequest
{
    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";
}

/// <summary>
/// A party's proposal for the next immutable contract draft. The server derives the land,
/// investor, landlord and offer IDs from the contract; the client only supplies the proposed
/// contractual terms and a reason. At least one term must differ from the current draft.
/// </summary>
public class CreateContractRevisionRequest
{
    /// <summary>The exact current draft the parties reviewed; a stale value is rejected with 409.</summary>
    [Range(1, int.MaxValue)]
    public int ExpectedDraftVersionId { get; set; }

    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";

    [Required, Range(typeof(decimal), "0.01", "100")]
    public decimal? LandlordSharePct { get; set; }

    [Range(1, 100)]
    public int? DurationYears { get; set; }

    public DateOnly? StartDate { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal? InstallationCost { get; set; }

    [Range(typeof(decimal), "0.01", "99999999.99")]
    public decimal? SolarCellCapacityKw { get; set; }

    [Range(typeof(decimal), "0", "999999999999.999")]
    public decimal? ExpectedAnnualRevenue { get; set; }

    [Range(typeof(decimal), "0", "999999999.999")]
    public decimal? EffectiveCostPerKw { get; set; }

    [Range(typeof(decimal), "0", "12000")]
    public decimal? PaybackPeriodMonths { get; set; }

    [Range(0, 3650)]
    public int? NoticePeriodDays { get; set; }
}

public class ContractRevisionConsentRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedDraftVersionId { get; set; }

    public bool Approved { get; set; }

    [NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string? Reason { get; set; }
}

public class ContractRevisionAdminDecisionRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedDraftVersionId { get; set; }

    /// <summary>Approve or Escalate.</summary>
    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(30)]
    public string Decision { get; set; } = "";

    [NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string? Reason { get; set; }
}

public class ContractRevisionSuperAdminDecisionRequest
{
    [Range(1, int.MaxValue)]
    public int ExpectedDraftVersionId { get; set; }

    /// <summary>Continue or Cancel.</summary>
    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(30)]
    public string Decision { get; set; } = "";

    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";
}

public class ContractRevisionConsentDto
{
    public int Id { get; set; }
    public int PartyUserId { get; set; }
    public string PartyName { get; set; } = "";
    public string PartyRole { get; set; } = "";
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ContractRevisionDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string Status { get; set; } = "";
    public int BaseDraftVersionId { get; set; }
    public int BaseDraftVersionNumber { get; set; }
    public ContractVersionTermsDto? ProposedTerms { get; set; }
    public string Reason { get; set; } = "";
    public int ProposedById { get; set; }
    public string ProposedByName { get; set; } = "";
    public string ProposedByRole { get; set; } = "";
    public DateTime ProposedAt { get; set; }

    public int? AdminReviewedById { get; set; }
    public string? AdminReviewedByName { get; set; }
    public string? AdminReviewedByRole { get; set; }
    public DateTime? AdminReviewedAt { get; set; }
    public string? AdminDecision { get; set; }
    public string? EscalationReason { get; set; }

    public int? SuperAdminDecidedById { get; set; }
    public string? SuperAdminDecidedByName { get; set; }
    public string? SuperAdminDecidedByRole { get; set; }
    public DateTime? SuperAdminDecidedAt { get; set; }
    public string? SuperAdminDecision { get; set; }
    public string? SuperAdminReason { get; set; }

    public int? NewOfferVersionId { get; set; }
    public int? NewOfferVersionNumber { get; set; }
    public int? NewDraftVersionId { get; set; }
    public int? NewDraftVersionNumber { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<ContractRevisionConsentDto> Consents { get; set; } = [];
}

public class ContractDto
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public int LandId { get; set; }
    public string LandLabel { get; set; } = "";
    public int InvestorId { get; set; }
    public string InvestorName { get; set; } = "";
    public int LandlordId { get; set; }
    public string LandlordName { get; set; } = "";
    public string Status { get; set; } = "";
    public int? OfferVersionId { get; set; }
    public int? CriterionId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? InvestorSignedAt { get; set; }
    public DateTime? LandlordSignedAt { get; set; }
    public DateTime? AdminReviewedAt { get; set; }
    public DateTime? AdminSignedAt { get; set; }
    public int NoticePeriodDays { get; set; }

    // Accepted offer-version terms (read-only snapshot of the approved version).
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public decimal? ExpectedAnnualRevenue { get; set; }
    public decimal? EffectiveCostPerKw { get; set; }
    public decimal? PaybackPeriodMonths { get; set; }
}

public class ContractReviewDto
{
    public int Id { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public int ReviewerId { get; set; }
    public string ReviewerName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Complete immutable terms snapshot stored with a contract version.</summary>
public class ContractVersionTermsDto
{
    public int? OfferVersionId { get; set; }
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public int LandlordId { get; set; }
    public int NoticePeriodDays { get; set; }
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public decimal? ExpectedAnnualRevenue { get; set; }
    public decimal? EffectiveCostPerKw { get; set; }
    public decimal? PaybackPeriodMonths { get; set; }
}

public class ContractVersionDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary><c>Draft</c> or <c>Event</c>.</summary>
    public string VersionKind { get; set; } = "";
    public string? EventType { get; set; }
    public string Status { get; set; } = "";
    public int? PreviousVersionId { get; set; }
    public int? TargetVersionId { get; set; }
    public int OfferId { get; set; }
    public int? OfferVersionId { get; set; }
    public ContractVersionTermsDto? Terms { get; set; }
    public string? Reason { get; set; }
    public int? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public string? CreatedByRole { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record ContractListResponse(
    IReadOnlyList<ContractDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);
