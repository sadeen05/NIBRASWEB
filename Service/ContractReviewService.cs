using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class ContractReviewService : IContractReviewService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<ContractReviewService> _logger;

    public ContractReviewService(NebrasdbContext context, ILogger<ContractReviewService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ContractReviewDto>> GetAllAsync()
    {
        var items = await _context.ContractReviews.ToListAsync();
        var result = new List<ContractReviewDto>();
        foreach (var item in items) result.Add(item.Adapt<ContractReviewDto>());
        return result;
    }

    public async Task<ContractReviewDto?> GetByIdAsync(int id)
    {
        var item = await _context.ContractReviews.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<ContractReviewDto>();
    }

    public async Task<ContractReviewDto> CreateAsync(CreateContractReviewRequest request)
    {
        // منع الإنشاء المباشر — ContractReview تُنشأ فقط من ContractService.AdminReviewAsync
        throw new InvalidOperationException(
            "Contract reviews cannot be created directly. Use the contract review endpoint instead.");
    }

    public async Task<bool> UpdateAsync(int id, UpdateContractReviewRequest request)
    {
        throw new InvalidOperationException("Contract reviews cannot be updated after creation.");
    }

    public async Task<bool> DeleteAsync(int id)
    {
        throw new InvalidOperationException("Contract reviews cannot be deleted.");
    }
}
