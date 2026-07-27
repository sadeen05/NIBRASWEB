using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferVersionService : IOfferVersionService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<OfferVersionService> _logger;

    private const decimal FilsToDinar = 1000m;

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
        var offer = await _context.Offers
            .Include(o => o.Status)
            .Include(o => o.Land).ThenInclude(l => l.Region).ThenInclude(r => r.TariffBrackets)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId);
        if (offer == null)
            throw new KeyNotFoundException("Offer not found.");

        var acceptedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Accepted");
        var rejectedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Rejected");
        var cancelledStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Cancelled");

        if (offer.StatusId == acceptedStatus.Id)
            throw new InvalidOperationException("Cannot add versions to an accepted offer.");
        if (offer.StatusId == rejectedStatus.Id)
            throw new InvalidOperationException("Cannot add versions to a rejected offer.");
        if (offer.StatusId == cancelledStatus.Id)
            throw new InvalidOperationException("Cannot add versions to a cancelled offer.");

        var maxVersion = await _context.OfferVersions
            .Where(v => v.OfferId == request.OfferId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        if (request.LandlordSharePct.HasValue &&
            (request.LandlordSharePct < 0 || request.LandlordSharePct > 100))
            throw new InvalidOperationException("Landlord share must be between 0 and 100.");

        var version = request.Adapt<OfferVersion>();
        version.VersionNumber = maxVersion + 1;

        if (request.SolarCellCapacityKw.HasValue)
        {
            CalculateRevenueProjections(version, offer);
        }

        _context.OfferVersions.Add(version);
        await _context.SaveChangesAsync();

        return version.Adapt<OfferVersionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferVersionRequest request)
    {
        var version = await _context.OfferVersions
            .Include(v => v.Offer).ThenInclude(o => o.Land).ThenInclude(l => l.Region).ThenInclude(r => r.TariffBrackets)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (version == null) return false;

        // منع التعديل إذا الإصدار مقبول في عقد
        var isLocked = await _context.Offers.AnyAsync(o => o.AcceptedVersionId == id);
        if (isLocked)
            throw new InvalidOperationException("Cannot modify a version that has been accepted into a contract.");

        if (request.LandlordSharePct.HasValue &&
            (request.LandlordSharePct < 0 || request.LandlordSharePct > 100))
            throw new InvalidOperationException("Landlord share must be between 0 and 100.");

        version.LandlordSharePct = request.LandlordSharePct;
        version.DurationYears = request.DurationYears;
        version.StartDate = request.StartDate;
        version.InstallationCost = request.InstallationCost;
        version.RejectionReason = request.RejectionReason;

        if (request.SolarCellCapacityKw.HasValue)
        {
            version.SolarCellCapacityKw = request.SolarCellCapacityKw;
            CalculateRevenueProjections(version, version.Offer);
        }

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

    private void CalculateRevenueProjections(OfferVersion version, Offer offer)
    {
        var region = offer.Land.Region;

        // الإنتاج السنوي
        var annualProduction = version.SolarCellCapacityKw!.Value * region.PeakSunHoursPerDay * 365m;

        // صافي الإنتاج بعد الفاقد
        var netProduction = annualProduction * (1m - region.LossPercentage / 100m);

        // الإنتاج الشهري (لأن الشرائح شهرية)
        var monthlyProduction = netProduction / 12m;

        // ترتيب الشرائح
        var brackets = region.TariffBrackets
            .Where(b => b.EffectiveFrom <= DateTime.UtcNow
                     && (b.EffectiveTo == null || b.EffectiveTo >= DateTime.UtcNow))
            .OrderBy(b => b.FromKwh)
            .ToList();

        // حساب الإيراد الشهري حسب الشرائح
        var monthlyRevenue = 0m;
        var remainingKwh = monthlyProduction;

        foreach (var bracket in brackets)
        {
            if (remainingKwh <= 0) break;

            var bracketSize = (bracket.ToKwh.HasValue ? bracket.ToKwh.Value : int.MaxValue) - bracket.FromKwh + 1;
            var kwhInBracket = Math.Min(bracketSize, remainingKwh);
            if (kwhInBracket <= 0) continue;

            monthlyRevenue += kwhInBracket * bracket.RatePerKwh / FilsToDinar;
            remainingKwh -= kwhInBracket;
        }

        // الإيراد السنوي
        var annualRevenue = monthlyRevenue * 12m;

        // EffectiveCostPerKw = متوسط السعر للكيلوواط - بدل العبور - الفاقد
        var avgRatePerKwh = netProduction > 0 ? annualRevenue / netProduction : 0m;
        var wheelingFeeInDinar = region.WheelingFeePerKwh / FilsToDinar;
        var lossCostInDinar = avgRatePerKwh * (region.LossPercentage / 100m);
        var effectiveCost = avgRatePerKwh - wheelingFeeInDinar - lossCostInDinar;

        // PaybackPeriodMonths
        decimal? paybackPeriod = null;
        if (version.InstallationCost.HasValue && version.InstallationCost > 0 && annualRevenue > 0)
        {
            paybackPeriod = Math.Round(version.InstallationCost.Value / annualRevenue * 12m, 2,
                MidpointRounding.AwayFromZero);
        }

        version.ExpectedAnnualRevenue = Math.Round(annualRevenue, 3, MidpointRounding.AwayFromZero);
        version.EffectiveCostPerKw = Math.Round(effectiveCost, 3, MidpointRounding.AwayFromZero);
        version.PaybackPeriodMonths = paybackPeriod;
    }
}
