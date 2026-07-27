using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandService : ILandService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandService> _logger;

    public LandService(NebrasdbContext context, ILogger<LandService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandDto>> GetAllAsync()
    {
        var lands = await _context.Lands
            .Include(l => l.LandStatus)
            .ToListAsync();

        var result = new List<LandDto>();
        foreach (var land in lands)
        {
            result.Add(land.Adapt<LandDto>());
        }
        return result;
    }

    public async Task<LandDto?> GetByIdAsync(int id)
    {
        var land = await _context.Lands
            .Include(l => l.LandStatus)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (land == null) return null;
        return land.Adapt<LandDto>();
    }

    public async Task<LandDto> CreateAsync(CreateLandRequest request)
    {
        // 1. المستخدم موجود ودوره Landlord
        var user = await _context.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.LandlordId);
        if (user == null)
            throw new InvalidOperationException("User not found.");
        if (user.Role.Name != "Landlord")
            throw new InvalidOperationException("Only landlords can register land.");

        // 2. التحقق من عدم التكرار
        var exists = await _context.Lands.AnyAsync(l =>
            l.LandNumber == request.LandNumber && l.RegionId == request.RegionId);
        if (exists)
            throw new InvalidOperationException("Land number already exists in this region.");

        // 3. إنشاء الأرض بحالة Draft
        var draftStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Draft");

        var land = request.Adapt<Land>();
        land.LandStatusId = draftStatus.Id;
        land.DataVerifiedByAdmin = false;
        land.IsDeleted = false;

        _context.Lands.Add(land);
        await _context.SaveChangesAsync();

        // 4. تسجيل أول حالة في السجل
        _context.LandStatusHistories.Add(new LandStatusHistory
        {
            LandId = land.Id,
            StatusId = draftStatus.Id,
            ChangedById = request.LandlordId,
            ChangedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return land.Adapt<LandDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandRequest request)
    {
        var land = await _context.Lands
            .Include(l => l.Contracts)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (land == null) return false;

        // الأرض تحت عقد لا يمكن تعديلها
        if (land.Contracts.Any(c => c.StatusId == 2)) // Active
            throw new InvalidOperationException("Cannot edit a land that has an active contract.");

        land.LandNumber = request.LandNumber;
        land.AreaDonum = request.AreaDonum;
        land.SlopePercentage = request.SlopePercentage;
        land.DistanceToGridKm = request.DistanceToGridKm;
        land.SolarIrradiance = request.SolarIrradiance;
        land.ElevationM = request.ElevationM;
        land.RegionId = request.RegionId;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // الحذف المباشر ممنوع — يتم فقط عبر DeletionRequest
        throw new InvalidOperationException("Direct deletion is not allowed. Use a deletion request instead.");
    }

    public async Task<bool> SubmitAsync(int landId)
    {
        var land = await _context.Lands.FindAsync(landId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var draftStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Draft");
        var pendingStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "PendingVerification");

        if (land.LandStatusId != draftStatus.Id)
            throw new InvalidOperationException($"Cannot submit land in status '{land.LandStatus.Name}'. Must be 'Draft'.");

        await ChangeStatusAsync(land, pendingStatus.Id, land.LandlordId, "Submitted for verification");
        return true;
    }

    public async Task<bool> VerifyAsync(int landId, int adminId)
    {
        var land = await _context.Lands
            .Include(l => l.LandDocuments)
            .ThenInclude(ld => ld.DocumentType)
            .FirstOrDefaultAsync(l => l.Id == landId);

        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var pendingStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "PendingVerification");

        if (land.LandStatusId != pendingStatus.Id)
            throw new InvalidOperationException("Land is not pending verification.");

        // التحقق من المستندات
        var hasTitleDeed = land.LandDocuments.Any(d =>
            d.DocumentType.Name == "TitleDeed" && d.Status == "Approved");
        if (!hasTitleDeed)
            throw new InvalidOperationException("No approved title deed document found.");

        // التحقق من المعايير
        var criteria = await _context.LandCriteria
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();
        if (criteria == null)
            throw new InvalidOperationException("No eligibility criteria configured.");

        var eligibility = await CheckEligibilityAsync(landId);
        if (!eligibility)
            throw new InvalidOperationException("Land does not meet eligibility criteria.");

        // تسجيل المعيار الذي تم التحقق بناءً عليه (لقطة تاريخية فقط — لا تؤثر على إعادة التحقق لاحقًا)
        land.VerifiedAgainstCriterionId = criteria.Id;

        var verifiedStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Verified");
        land.DataVerifiedByAdmin = true;

        await ChangeStatusAsync(land, verifiedStatus.Id, adminId, "Verified by admin");
        return true;
    }

    public async Task<bool> RejectAsync(int landId, int adminId, string reason)
    {
        var land = await _context.Lands.FindAsync(landId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var pendingStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "PendingVerification");
        if (land.LandStatusId != pendingStatus.Id)
            throw new InvalidOperationException("Land is not pending verification.");

        var rejectedStatus = await _context.LandStatuses.FirstAsync(s => s.Name == "Rejected");
        await ChangeStatusAsync(land, rejectedStatus.Id, adminId, reason);
        return true;
    }

    public async Task<bool> AddDocumentAsync(int landId, CreateLandDocumentRequest request)
    {
        var land = await _context.Lands.FindAsync(landId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var docType = await _context.DocumentTypes.FindAsync(request.DocumentTypeId);
        if (docType == null)
            throw new KeyNotFoundException("Document type not found.");

        var latestVersion = await _context.LandDocuments
            .Where(d => d.LandId == landId && d.DocumentTypeId == request.DocumentTypeId)
            .OrderByDescending(d => d.Version)
            .Select(d => (int?)d.Version)
            .FirstOrDefaultAsync();

        var doc = new LandDocument
        {
            LandId = landId,
            DocumentTypeId = request.DocumentTypeId,
            FilePath = request.FilePath,
            Version = (latestVersion ?? 0) + 1,
            Status = "Pending",
            UploadedAt = DateTime.UtcNow
        };

        _context.LandDocuments.Add(doc);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<LandDocumentDto>> GetDocumentsAsync(int landId)
    {
        var docs = await _context.LandDocuments
            .Where(d => d.LandId == landId)
            .ToListAsync();

        var result = new List<LandDocumentDto>();
        foreach (var doc in docs)
        {
            result.Add(doc.Adapt<LandDocumentDto>());
        }
        return result;
    }

    public async Task<bool> CheckEligibilityAsync(int landId)
    {
        var land = await _context.Lands.FindAsync(landId);
        if (land == null)
            throw new KeyNotFoundException("Land not found.");

        var criteria = await _context.LandCriteria
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        if (criteria == null)
            throw new InvalidOperationException("No eligibility criteria configured.");

        if (land.AreaDonum < criteria.MinAreaDonum) return false;
        if (land.SlopePercentage > criteria.MaxSlopePct) return false;
        if (land.DistanceToGridKm > criteria.MaxGridDistanceKm) return false;
        if (land.SolarIrradiance < criteria.MinSolarIrradiance) return false;
        if (land.ElevationM < criteria.MinElevationM) return false;

        return true;
    }

    private async Task ChangeStatusAsync(Land land, int newStatusId, int changedById, string? reason)
    {
        land.LandStatusId = newStatusId;

        _context.LandStatusHistories.Add(new LandStatusHistory
        {
            LandId = land.Id,
            StatusId = newStatusId,
            ChangedById = changedById,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }
}
