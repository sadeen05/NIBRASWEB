using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IContractReviewService
{
    Task<List<ContractReviewDto>> GetAllAsync();
    Task<ContractReviewDto?> GetByIdAsync(int id);
    Task<ContractReviewDto> CreateAsync(CreateContractReviewRequest request);
    Task<bool> UpdateAsync(int id, UpdateContractReviewRequest request);
    Task<bool> DeleteAsync(int id);
}
