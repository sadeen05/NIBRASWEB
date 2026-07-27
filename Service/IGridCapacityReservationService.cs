using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IGridCapacityReservationService
{
    Task<List<GridCapacityReservationDto>> GetAllAsync();
    Task<GridCapacityReservationDto?> GetByIdAsync(int id);
    Task<GridCapacityReservationDto> CreateAsync(CreateGridCapacityReservationRequest request);
    Task<bool> UpdateAsync(int id, UpdateGridCapacityReservationRequest request);
    Task<bool> DeleteAsync(int id);
}
