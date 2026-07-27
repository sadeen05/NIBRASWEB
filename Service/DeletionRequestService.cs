using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class DeletionRequestService : IDeletionRequestService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<DeletionRequestService> _logger;

    public DeletionRequestService(NebrasdbContext context, ILogger<DeletionRequestService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<DeletionRequestDto>> GetAllAsync()
    {
        var items = await _context.DeletionRequests.ToListAsync();
        var result = new List<DeletionRequestDto>();
        foreach (var item in items) result.Add(item.Adapt<DeletionRequestDto>());
        return result;
    }

    public async Task<DeletionRequestDto?> GetByIdAsync(int id)
    {
        var item = await _context.DeletionRequests.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<DeletionRequestDto>();
    }

    public async Task<DeletionRequestDto> CreateAsync(CreateDeletionRequestRequest request)
    {
        // 1. الأرض موجودة
        var land = await _context.Lands.FindAsync(request.LandId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        // 2. الأرض ما عليها Contract Active
        var hasActiveContract = await _context.Contracts.AnyAsync(c =>
            c.LandId == request.LandId && c.StatusId == 2);
        if (hasActiveContract)
            throw new InvalidOperationException(
                "Cannot delete land that has an active contract. Terminate the contract first.");

        // 3. تأكد ما في طلب حذفPending
        var hasPending = await _context.DeletionRequests.AnyAsync(d =>
            d.LandId == request.LandId && d.Status == "Pending");
        if (hasPending)
            throw new InvalidOperationException("A pending deletion request already exists for this land.");

        var item = new DeletionRequest
        {
            LandId = request.LandId,
            RequestedById = land.LandlordId,
            Reason = request.Reason,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.DeletionRequests.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<DeletionRequestDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateDeletionRequestRequest request)
    {
        var item = await _context.DeletionRequests
            .Include(d => d.Land)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (item == null) return false;

        if (item.Status != "Pending")
            throw new InvalidOperationException("Only pending requests can be updated.");

        if (request.Status == "Approved")
        {
            item.Land.IsDeleted = true;
            _logger.LogInformation("Land {LandId} soft-deleted via deletion request.", item.LandId);
        }

        item.Status = request.Status;
        item.AdminComment = request.AdminComment;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.DeletionRequests.FindAsync(id);
        if (item == null) return false;

        _context.DeletionRequests.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
