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
    public string ReviewerName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
}

public record ContractListResponse(
    IReadOnlyList<ContractDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);
