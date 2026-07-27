using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferVersionService : IOfferVersionService
{
    private readonly IRepository<OfferVersion> _offerVersionRepository;
    private readonly ILogger<OfferVersionService> _logger;

    public OfferVersionService(IRepository<OfferVersion> offerVersionRepository, ILogger<OfferVersionService> logger)
    {
        _offerVersionRepository = offerVersionRepository;
        _logger = logger;
    }

    public async Task<List<OfferVersionDto>> GetAllAsync()
    {
        var versions = await _offerVersionRepository.GetAllAsync();
        var result = new List<OfferVersionDto>();

        foreach (var version in versions)
        {
            result.Add(version.Adapt<OfferVersionDto>());
        }

        return result;
    }

    public async Task<OfferVersionDto?> GetByIdAsync(int id)
    {
        var version = await _offerVersionRepository.GetByIdAsync(id);
        if (version == null) return null;
        return version.Adapt<OfferVersionDto>();
    }

    public async Task<OfferVersionDto> CreateAsync(CreateOfferVersionRequest request)
    {
        var version = request.Adapt<OfferVersion>();
        await _offerVersionRepository.AddAsync(version);
        await _offerVersionRepository.SaveChangesAsync();
        return version.Adapt<OfferVersionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferVersionRequest request)
    {
        var version = await _offerVersionRepository.GetByIdAsync(id);
        if (version == null) return false;

        version.LandlordSharePct = request.LandlordSharePct;
        version.DurationYears = request.DurationYears;
        version.StartDate = request.StartDate;
        version.InstallationCost = request.InstallationCost;
        version.RejectionReason = request.RejectionReason;

        await _offerVersionRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _offerVersionRepository.DeleteAsync(id);
        if (result == false) return false;
        await _offerVersionRepository.SaveChangesAsync();
        return true;
    }
}
