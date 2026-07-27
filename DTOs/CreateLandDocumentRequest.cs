namespace NIBRAS.API.DTOs;

public class CreateLandDocumentRequest
{
    public int LandId { get; set; }
    public int DocumentTypeId { get; set; }
    public string FilePath { get; set; } = "";
}
