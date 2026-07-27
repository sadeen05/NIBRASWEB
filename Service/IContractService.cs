using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IContractService
{
    Task<List<ContractDto>> GetAllAsync();
    Task<ContractDto?> GetByIdAsync(int id);
    Task<ContractDto> CreateAsync(CreateContractRequest request);
    Task<bool> UpdateAsync(int id, UpdateContractRequest request);
    Task<bool> DeleteAsync(int id);

    Task<bool> SignAsInvestorAsync(int contractId, int userId);
    Task<bool> SignAsLandlordAsync(int contractId, int userId);
    Task<ContractReviewDto> AdminReviewAsync(int contractId, int adminId, string decision, string? reason);
    Task<bool> TerminateAsync(int contractId, int adminId, string reason);
}
