using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IContractService
{
    Task<ContractListResponse> GetAllAsync(int pageNumber, int pageSize, string? status);

    Task<ContractDto> GetByIdAsync(int id);

    Task<ContractDto> CreateAsync(CreateContractRequest request);

    Task<ContractDto> SignAsync(int id);

    Task<ContractDto> ReviewAsync(int id, ReviewContractRequest request);

    Task<ContractDto> TerminateAsync(int id, TerminateContractRequest request);

    Task<List<ContractReviewDto>> GetReviewsAsync(int id);
}
