using System.ComponentModel.DataAnnotations;
namespace NIBRAS.API.DTOs;

public sealed class RejectInvestorApprovalRequest
{
    [Required, MaxLength(500), NoControlCharactersExceptTabAndNewline]
    public string Reason { get; set; } = "";
}
