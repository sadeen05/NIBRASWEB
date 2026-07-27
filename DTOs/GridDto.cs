namespace NIBRAS.API.DTOs;

public class GridDto
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = "";
    public decimal CapacityMw { get; set; }
    public string? Status { get; set; }
}
