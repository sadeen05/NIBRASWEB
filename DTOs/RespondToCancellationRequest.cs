namespace NIBRAS.API.DTOs;

public class RespondToCancellationRequest
{
    public int UserId { get; set; }
    public bool Agree { get; set; }
}
