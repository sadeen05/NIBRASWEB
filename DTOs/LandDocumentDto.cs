namespace NIBRAS.API.DTOs;

public class LandDocumentDto
{
    public int Id { get; set; }
    public int LandId { get; set; }
    public int DocumentTypeId { get; set; }
    public string FilePath { get; set; } = "";
    public int Version { get; set; }
    public string? Status { get; set; }
    public DateTime? UploadedAt { get; set; }
}
