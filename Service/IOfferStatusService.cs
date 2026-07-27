using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IOfferStatusService
{
    Task<List<OfferStatusDto>> GetAllAsync();
    Task<OfferStatusDto?> GetByIdAsync(int id);
    Task<OfferStatusDto> CreateAsync(CreateOfferStatusRequest request);
    Task<bool> UpdateAsync(int id, UpdateOfferStatusRequest request);
    Task<bool> DeleteAsync(int id);
}
