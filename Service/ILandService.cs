using Microsoft.AspNetCore.Http;
using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface ILandService
{
    Task<LandListResponse> GetAllAsync(int pageNumber, int pageSize, string? status);

    Task<LandDto> GetByIdAsync(int id);

    Task<LandDto> CreateAsync(CreateLandRequest request);

    Task UpdateAsync(int id, UpdateLandRequest request);

    Task SubmitAsync(int id);

    Task ReviseAsync(int id);

    /// <param name="note">
    /// Optional administrator approval note. It is appended to the recorded status-history
    /// reason; <c>null</c> or blank leaves the recorded reason exactly as before.
    /// </param>
    Task VerifyAsync(int id, string? note = null);

    Task RejectAsync(int id, string reason);

    Task<LandEligibilityResponse> GetEligibilityAsync(int id);

    Task<List<LandStatusHistoryDto>> GetAllStatusHistoryAsync(int landId);

    Task<LandDocumentDto> UploadDocumentAsync(int landId, UploadLandDocumentRequest request);

    Task<LandDocumentDto> UploadDocumentAsync(int landId, int documentTypeId, IFormFile file);

    Task<LandDocumentDownload> DownloadDocumentAsync(int landId, int documentId);

    Task<List<LandDocumentDto>> GetDocumentsAsync(int landId);

    Task<LandDocumentDto> ReviewDocumentAsync(int landId, int documentId, ReviewLandDocumentRequest request);

    Task<LandDto> AssessAsync(int landId, AssessLandRequest request);

    Task<List<LandAssessmentDto>> GetAssessmentsAsync(int landId);

    /// <summary>Landlord change request for an approved land; hides the land until reviewed.</summary>
    Task<LandChangeRequestDto> SubmitChangeRequestAsync(CreateLandChangeRequestRequest request);

    /// <summary>Change requests of one land (owner or Admin/SuperAdmin).</summary>
    Task<List<LandChangeRequestDto>> GetChangeRequestsAsync(int landId);

    /// <summary>The SuperAdmin queue of change requests still awaiting a decision.</summary>
    Task<List<LandChangeRequestDto>> GetPendingChangeRequestsAsync();

    /// <summary>SuperAdmin decision; approval applies the change, rejection preserves the original.</summary>
    Task<LandChangeRequestDto> DecideChangeRequestAsync(int changeRequestId, ReviewLandChangeRequestRequest request);
}
