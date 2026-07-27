using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferStatusService : IOfferStatusService
{
    private readonly IRepository<OfferStatus> _offerStatusRepository;
    private readonly ILogger<OfferStatusService> _logger;

    public OfferStatusService(IRepository<OfferStatus> offerStatusRepository, ILogger<OfferStatusService> logger)
    {
        _offerStatusRepository = offerStatusRepository;
        _logger = logger;
    }

    public async Task<List<OfferStatusDto>> GetAllAsync()
    {
        var statuses = await _offerStatusRepository.GetAllAsync();
        var result = new List<OfferStatusDto>();

        foreach (var status in statuses)
        {
            result.Add(new OfferStatusDto
            {
                Id = status.Id,
                Name = status.NameStatus
            });
        }

        return result;
    }

    public async Task<OfferStatusDto?> GetByIdAsync(int id)
    {
        var status = await _offerStatusRepository.GetByIdAsync(id);
        if (status == null) return null;

        return new OfferStatusDto
        {
            Id = status.Id,
            Name = status.NameStatus
        };
    }

    public async Task<OfferStatusDto> CreateAsync(CreateOfferStatusRequest request)
    {
        var status = new OfferStatus
        {
            NameStatus = request.Name
        };
        await _offerStatusRepository.AddAsync(status);
        await _offerStatusRepository.SaveChangesAsync();
        return new OfferStatusDto
        {
            Id = status.Id,
            Name = status.NameStatus
        };
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferStatusRequest request)
    {
        var status = await _offerStatusRepository.GetByIdAsync(id);
        if (status == null) return false;

        status.NameStatus = request.Name;

        await _offerStatusRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _offerStatusRepository.DeleteAsync(id);
        if (result == false) return false;
        await _offerStatusRepository.SaveChangesAsync();
        return true;
    }
}
