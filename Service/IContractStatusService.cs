using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IContractStatusService
{
    Task<List<ContractStatusDto>> GetAllAsync();
    Task<ContractStatusDto?> GetByIdAsync(int id);
    Task<ContractStatusDto> CreateAsync(CreateContractStatusRequest request);
    Task<bool> UpdateAsync(int id, UpdateContractStatusRequest request);
    Task<bool> DeleteAsync(int id);
}
