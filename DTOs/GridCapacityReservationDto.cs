namespace NIBRAS.API.DTOs;

public class GridCapacityReservationDto
{
    public int Id { get; set; }
    public int GridId { get; set; }
    public int OfferId { get; set; }
    public int? ContractId { get; set; }
    public string? ReservationType { get; set; }
    public decimal ReservedMw { get; set; }
    public DateTime? CreatedAt { get; set; }
}
