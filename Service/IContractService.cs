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

    Task<List<ContractVersionDto>> GetVersionsAsync(int id);

    Task<List<ContractRevisionDto>> GetRevisionsAsync(int id);

    Task<ContractRevisionDto> RequestRevisionAsync(int id, CreateContractRevisionRequest request);

    Task<ContractRevisionDto> ConsentToRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionConsentRequest request);

    Task<ContractRevisionDto> AdminReviewRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionAdminDecisionRequest request);

    Task<ContractRevisionDto> SuperAdminDecideRevisionAsync(
        int id,
        int revisionId,
        ContractRevisionSuperAdminDecisionRequest request);
}
