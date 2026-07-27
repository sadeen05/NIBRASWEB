using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IGridService
{
    Task<List<GridDto>> GetAllAsync();
    Task<GridDto?> GetByIdAsync(int id);
    Task<GridDto> CreateAsync(CreateGridRequest request);
    Task<bool> UpdateAsync(int id, UpdateGridRequest request);
    Task<bool> DeleteAsync(int id);

}