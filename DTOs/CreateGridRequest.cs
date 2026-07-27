namespace NIBRAS.API.DTOs;

public class CreateGridRequest
{
    public int RegionId { get; set; }
    public string Name { get; set; } = "";
    public decimal CapacityMw { get; set; }
    public string? Status { get; set; }
}
