using NIBRAS.API.DTOs;
using NibrasWeb.DTOs;

namespace NibrasWeb.Service
{
    public interface IOfferService
    {
        Task<OfferDto?> GetByIdAsync(int offerId);
        Task<List<OfferDto>> GetOffersForLandAsync(int landId, int requestingUserId);
        Task<List<OfferDto>> GetOfferDtosAsync(int investorId, string? statusFilter = null);
        Task<List<OfferDto>> GetPendingAdminApprovalAsync();

        Task<OfferDto> CreateAsync(CreateOfferRequest request);
        Task<OfferDto> CounterOfferAsync(int offerId, int userId, decimal newAmount, string? message);
        Task<OfferDto> AcceptAsync(int offerId, int userId);
        Task<OfferDto> RejectAsync(int offerId, int userId, string reason);
        Task<OfferDto> WithdrawAsync(int offerId, int investorId);
        Task<OfferDto> AdminApproveAsync(int offerId, int adminId);
        Task<OfferDto> AdminRejectAsync(int offerId, int adminId, string reason);

        Task<List<OfferNegotiationHistoryDto>> GetNegotiationHistoryAsync(int offerId, int requestingUserId);
        Task MarkOfferClosedAsync(int offerId, int contractId, int performedByUserId);
    }
}
