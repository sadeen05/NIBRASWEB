using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferService : IOfferService
{
    private readonly IRepository<Offer> _offerRepository;
    private readonly ILogger<OfferService> _logger;

    public OfferService(IRepository<Offer> offerRepository, ILogger<OfferService> logger)
    {
        _offerRepository = offerRepository;
        _logger = logger;
    }

    public async Task<List<OfferDto>> GetAllAsync()
    {
        var offers = await _offerRepository.GetAllAsync();
        var result = new List<OfferDto>();

        foreach (var offer in offers)
        {
            result.Add(offer.Adapt<OfferDto>());
        }

        return result;
    }

    public async Task<OfferDto?> GetByIdAsync(int id)
    {
        var offer = await _offerRepository.GetByIdAsync(id);
        if (offer == null) return null;
        return offer.Adapt<OfferDto>();
    }

    public async Task<OfferDto> CreateAsync(CreateOfferRequest request)
    {
        var offer = request.Adapt<Offer>();
        await _offerRepository.AddAsync(offer);
        await _offerRepository.SaveChangesAsync();
        return offer.Adapt<OfferDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferRequest request)
    {
        var offer = await _offerRepository.GetByIdAsync(id);
        if (offer == null) return false;

        offer.LandId = request.LandId;
        offer.InvestorId = request.InvestorId;
        offer.RequiredCapacityMw = request.RequiredCapacityMw;
        offer.StatusId = request.StatusId;

        await _offerRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var offer = await _offerRepository.GetByIdAsync(id);
        if (offer == null) return false;

        offer.IsDeleted = true;
        await _offerRepository.SaveChangesAsync();
        return true;
    }
}
