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

    // Cancellation flow
    Task<bool> RequestCancellationAsync(int contractId, int userId, string reason, decimal? investorPenaltyAmount);
    Task<bool> RespondToCancellationAsync(int contractId, int userId, bool agree);

    // Admin force termination (bypasses dispute guard, requires SuperAdmin)
    Task<bool> AdminForceTerminateAsync(int contractId, int adminId, string justification, decimal? compensationOverride);
}
