namespace NIBRAS.API.DTOs;

public class CreateDeletionRequestRequest
{
    public int LandId { get; set; }
    public string Reason { get; set; } = "";
}
