using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class DocumentTypeRequest
{
    [Required, NoControlCharacters, MaxLength(100)]
    public string Name { get; set; } = "";

    public bool IsRequiredForVerification { get; set; }
}

public record DocumentTypeDto(int Id, string Name, bool IsRequiredForVerification);
