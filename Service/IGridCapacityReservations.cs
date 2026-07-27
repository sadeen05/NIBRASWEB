using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IGridCapactyService
{
    Task<List<GridGridCapacityReservationsDto>> GetAllAsync();
    Task<GridGridCapacityReservationsDto?> GetByIdAsync(int id);
    Task<GridGridCapacityReservationsDto> CreateAsync(Create IGridCapacityReservationsRequest request);
    Task<bool> UpdateAsync(int id, UpdateI GridCapacityReservationsRequest request);
    Task<bool> DeleteAsync(int id);
}