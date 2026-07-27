using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferStatusService : IOfferStatusService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<OfferStatusService> _logger;

    public OfferStatusService(NebrasdbContext context, ILogger<OfferStatusService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<OfferStatusDto>> GetAllAsync()
    {
        var statuses = await _context.OfferStatuses.ToListAsync();
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
        var status = await _context.OfferStatuses.FindAsync(id);
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
        _context.OfferStatuses.Add(status);
        await _context.SaveChangesAsync();
        return new OfferStatusDto
        {
            Id = status.Id,
            Name = status.NameStatus
        };
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferStatusRequest request)
    {
        var status = await _context.OfferStatuses.FindAsync(id);
        if (status == null) return false;

        status.NameStatus = request.Name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var status = await _context.OfferStatuses.FindAsync(id);
        if (status == null) return false;
        _context.OfferStatuses.Remove(status);
        await _context.SaveChangesAsync();
        return true;
    }
}
