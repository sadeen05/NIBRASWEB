using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class OfferService : IOfferService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<OfferService> _logger;

    public OfferService(NebrasdbContext context, ILogger<OfferService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<OfferDto>> GetAllAsync()
    {
        var offers = await _context.Offers.ToListAsync();
        var result = new List<OfferDto>();
        foreach (var offer in offers) result.Add(offer.Adapt<OfferDto>());
        return result;
    }

    public async Task<OfferDto?> GetByIdAsync(int id)
    {
        var offer = await _context.Offers.FindAsync(id);
        if (offer == null) return null;
        return offer.Adapt<OfferDto>();
    }

    public async Task<OfferDto> CreateAsync(CreateOfferRequest request)
    {
        // 1. الأرض موجودة وحالتها Verified
        var land = await _context.Lands
            .Include(l => l.LandStatus)
            .FirstOrDefaultAsync(l => l.Id == request.LandId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var verifiedStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Verified");
        if (land.LandStatusId != verifiedStatus.Id)
            throw new InvalidOperationException("Land is not available for offers.");

        // 2. الأرض ما عليها Contract Active
        var hasActiveContract = await _context.Contracts.AnyAsync(c =>
            c.LandId == request.LandId && c.StatusId == 2);
        if (hasActiveContract)
            throw new InvalidOperationException("Land already has an active contract.");

        // 3. منع Self-Dealing
        if (land.LandlordId == request.InvestorId)
            throw new InvalidOperationException("Investor cannot be the landlord of this land.");

        // 4. أنشئ العرض بحالة "Submitted"
        var submittedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Submitted");

        var offer = request.Adapt<Offer>();
        offer.StatusId = submittedStatus.Id;

        _context.Offers.Add(offer);
        await _context.SaveChangesAsync();

        return offer.Adapt<OfferDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateOfferRequest request)
    {
        var offer = await _context.Offers.FindAsync(id);
        if (offer == null) return false;

        // State Machine للـ Offer
        var submittedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Submitted");
        var underNegStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "UnderNegotiation");
        var acceptedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Accepted");
        var rejectedStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Rejected");
        var cancelledStatus = await _context.OfferStatuses.FirstAsync(s => s.NameStatus == "Cancelled");

        // الحالة الجديدة المطلوبة
        var newStatus = await _context.OfferStatuses.FindAsync(request.StatusId);
        if (newStatus == null)
            throw new KeyNotFoundException("Status not found.");

        // التحقق من الـ Transition
        var allowed = false;

        // Cancel يسمح دائمًا
        if (newStatus.Id == cancelledStatus.Id && offer.IsDeleted == false)
            allowed = true;
        else if (newStatus.Id == acceptedStatus.Id && offer.StatusId == underNegStatus.Id)
            allowed = true;
        else if (newStatus.Id == rejectedStatus.Id &&
                 (offer.StatusId == underNegStatus.Id || offer.StatusId == submittedStatus.Id))
            allowed = true;
        else if (newStatus.Id == underNegStatus.Id && offer.StatusId == submittedStatus.Id)
            allowed = true;

        if (!allowed)
            throw new InvalidOperationException($"Cannot transition from '{offer.Status.NameStatus}' to '{newStatus.NameStatus}'.");

        // عدّ الرفض
        if (newStatus.Id == rejectedStatus.Id)
        {
            var rejectionCount = await _context.OfferVersions
                .CountAsync(v => v.OfferId == id && v.RejectionReason != null);

            if (rejectionCount >= 3)
            {
                offer.IsDeleted = true;
                _logger.LogWarning("Offer auto-cancelled after 3 rejections.");
            }
        }

        // خفّض الأرض إذا قبل
        if (newStatus.Id == acceptedStatus.Id)
        {
            var land = await _context.Lands.FindAsync(offer.LandId);
            if (land != null)
            {
                var underOfferStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "UnderOffer");
                land.LandStatusId = underOfferStatus.Id;
            }
        }

        offer.LandId = request.LandId;
        offer.InvestorId = request.InvestorId;
        offer.RequiredCapacityMw = request.RequiredCapacityMw;
        offer.StatusId = request.StatusId;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var offer = await _context.Offers.FindAsync(id);
        if (offer == null) return false;

        offer.IsDeleted = true;
        await _context.SaveChangesAsync();
        return true;
    }
}
