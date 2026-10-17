using NIBRAS.API.DTOs;
using NibrasWeb.DTOs;

namespace NibrasWeb.Service
{
    public interface IOfferService
    {
        Task<OfferDto> GetByIdAsync(int offerId);
        Task<List<OfferDto>> GetOffersForLandAsync(int landId);
        Task<List<OfferDto>> GetMyOffersAsync(string? statusFilter = null);
        Task<List<OfferDto>> GetPendingAdminApprovalAsync();

        Task<OfferDto> CreateAsync(CreateOfferRequest request);
        Task<OfferDto> CounterOfferAsync(int offerId, CounterOfferRequest request);
        Task<OfferDto> AcceptAsync(int offerId);
        Task<OfferDto> RejectAsync(int offerId, string reason);
        Task<OfferDto> WithdrawAsync(int offerId);
        Task<OfferDto> AdminApproveAsync(int offerId);
        Task<OfferDto> AdminRejectAsync(int offerId, string reason);

        Task<List<OfferNegotiationHistoryDto>> GetNegotiationHistoryAsync(int offerId);
        Task MarkOfferClosedAsync(int offerId, int contractId, int performedByUserId);
    }
}