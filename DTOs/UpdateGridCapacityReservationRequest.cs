namespace NIBRAS.API.DTOs;

public class UpdateGridCapacityReservationRequest
{
    public int GridId { get; set; }
    public int ContractId { get; set; }
    public decimal ReservedMw { get; set; }
}
