using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IOfferVersionService
{
    Task<List<OfferVersionDto>> GetAllAsync();
    Task<OfferVersionDto?> GetByIdAsync(int id);
    Task<OfferVersionDto> CreateAsync(CreateOfferVersionRequest request);
    Task<bool> UpdateAsync(int id, UpdateOfferVersionRequest request);
    Task<bool> DeleteAsync(int id);
   
}
