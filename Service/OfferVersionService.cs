using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferVersionService : IOfferVersionService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<OfferVersionService> _logger;

    public OfferVersionService(NebrasdbContext context, ILogger<OfferVersionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<OfferVersionDto>> GetAllAsync()
    {
        var versions = await _context.OfferVersions.ToListAsync();
        var result = new List<OfferVersionDto>();
        foreach (var v in versions) result.Add(v.Adapt<OfferVersionDto>());
        return result;
    }

    public async Task<OfferVersionDto?> GetByIdAsync(int id)
    {
        var version = await _context.OfferVersions.FindAsync(id);
        if (version == null) return null;
        return version.Adapt<OfferVersionDto>();
    }

    public async Task<OfferVersionDto> CreateAsync(CreateOfferVersionRequest request)
    {
        // 1. التأكد من وجود الـ Offer
        var offer = await _context.Offers
            .Include(o => o.Status)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId);
        if (offer == null)
            throw new KeyNotFoundException("Offer not found.");

        // 2. Offer لسا قيد التفاوض فقط
        var acceptedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Accepted");
        var rejectedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Rejected");
        var cancelledStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Cancelled");

        if (offer.StatusId == acceptedStatus.Id)
            throw new InvalidOperationException("Cannot add versions to an accepted offer.");
        if (offer.StatusId == rejectedStatus.Id)
            throw new InvalidOperationException("Cannot add versions to a rejected offer.");
        if (offer.StatusId == cancelledStatus.Id)
            throw new InvalidOperationException("Cannot add versions to a cancelled offer.");

        // 3. VersionNumber تلقائي
        var maxVersion = await _context.OfferVersions
            .Where(v => v.OfferId == request.OfferId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        // 4. التحقق من LandlordSharePct
        if (request.LandlordSharePct.HasValue &&
            (request.LandlordSharePct < 0 || request.LandlordSharePct > 100))
            throw new InvalidOperationException("Landlord share must be between 0 and 100.");

        var version = request.Adapt<OfferVersion>();
        version.VersionNumber = maxVersion + 1;

        _context.OfferVersions.Add(version);
        await _context.SaveChangesAsync();

        return version.Adapt<OfferVersionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferVersionRequest request)
    {
        var version = await _context.OfferVersions.FindAsync(id);
        if (version == null) return false;

        if (request.LandlordSharePct.HasValue &&
            (request.LandlordSharePct < 0 || request.LandlordSharePct > 100))
            throw new InvalidOperationException("Landlord share must be between 0 and 100.");

        version.LandlordSharePct = request.LandlordSharePct;
        version.DurationYears = request.DurationYears;
        version.StartDate = request.StartDate;
        version.InstallationCost = request.InstallationCost;
        version.RejectionReason = request.RejectionReason;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _context.OfferVersions.FindAsync(id);
        if (result == null) return false;
        _context.OfferVersions.Remove(result);
        await _context.SaveChangesAsync();
        return true;
    }
}
