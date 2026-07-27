using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface ITariffBracketService
{
    Task<List<TariffBracketDto>> GetAllAsync();
    Task<TariffBracketDto?> GetByIdAsync(int id);
    Task<TariffBracketDto> CreateAsync(CreateTariffBracketRequest request);
    Task<bool> UpdateAsync(int id, UpdateTariffBracketRequest request);
    Task<bool> DeleteAsync(int id);
}
