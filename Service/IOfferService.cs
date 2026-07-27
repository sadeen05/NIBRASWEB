using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;


public interface IOfferService
{
    Task<List<OfferDto>> GetAllAsync();
    Task<OfferDto?> GetByIdAsync(int id);
    Task<OfferDto> CreateAsync(CreateOfferRequest request);
    Task<bool> UpdateAsync(int id, UpdateOfferRequest request);
    Task<bool> DeleteAsync(int id);
}
