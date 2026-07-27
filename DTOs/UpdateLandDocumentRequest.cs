namespace NIBRAS.API.DTOs;

public class UpdateLandDocumentRequest
{
    public int DocumentTypeId { get; set; }
    public string FilePath { get; set; } = "";
    public string? Status { get; set; }
}
