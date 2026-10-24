using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LandsController : ControllerBase
{
    private readonly ILandService _landService;
    private readonly IWebHostEnvironment _environment;

    public LandsController(ILandService landService, IWebHostEnvironment environment)
    {
        _landService = landService;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<LandListResponse>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        return Ok(await _landService.GetAllAsync(pageNumber, pageSize, status));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandDto>> GetById(int id)
    {
        return Ok(await _landService.GetByIdAsync(id));
    }

    [HttpPost]
    public async Task<ActionResult<LandDto>> Create(CreateLandRequest request)
    {
        var land = await _landService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = land.Id }, land);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandRequest request)
    {
        await _landService.UpdateAsync(id, request);
        return NoContent();
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(int id)
    {
        await _landService.SubmitAsync(id);
        return Ok(new { message = "Land submitted for verification." });
    }

    [HttpPost("{id}/revise")]
    public async Task<IActionResult> Revise(int id)
    {
        await _landService.ReviseAsync(id);
        return Ok(new { message = "Land returned to Draft." });
    }

    /// <summary>
    /// Admin/SuperAdmin verification decision. The request body is optional and may carry
    /// an approval note; when it is omitted (or empty) the previous behaviour is unchanged.
    /// The note is stored in the append-only land-status history reason, next to the
    /// criterion version the decision was taken against.
    /// </summary>
    [HttpPost("{id}/verify")]
    public async Task<IActionResult> Verify(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VerifyLandRequest? request = null)
    {
        await _landService.VerifyAsync(id, request?.Note);
        return Ok(new { message = "Land verified successfully." });
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(int id, RejectLandRequest request)
    {
        await _landService.RejectAsync(id, request.Reason);
        return Ok(new { message = "Land rejected." });
    }

    [HttpGet("{id}/eligibility")]
    public async Task<ActionResult<LandEligibilityResponse>> GetEligibility(int id)
    {
        return Ok(await _landService.GetEligibilityAsync(id));
    }

    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<LandStatusHistoryDto>>> GetHistory(int id)
    {
        return Ok(await _landService.GetAllStatusHistoryAsync(id));
    }

    /// <summary>
    /// The real document upload. The body is multipart form data: the document type and the
    /// file. The storage service validates the size and the magic bytes, so the browser-supplied
    /// content type and file extension are never trusted.
    /// </summary>
    [HttpPost("{id}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentStorageOptions.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentStorageOptions.MaxUploadRequestBytes)]
    public async Task<ActionResult<LandDocumentDto>> UploadDocument(
        int id,
        [FromForm] UploadLandDocumentFileRequest request)
    {
        if (request.File is null)
        {
            return BadRequest(new { message = "The document file is required." });
        }

        var document = await _landService.UploadDocumentAsync(id, request.DocumentTypeId, request.File);
        return Created($"/api/lands/{id}/documents", document);
    }

    /// <summary>
    /// Legacy metadata-only registration. It is accepted only in Development, for the historical
    /// regression suites; every deployed environment uses the multipart upload above. It has its
    /// own route so the multipart upload and this legacy action cannot collide in OpenAPI.
    /// </summary>
    [HttpPost("{id}/documents/metadata")]
    [Consumes("application/json")]
    public async Task<ActionResult<LandDocumentDto>> UploadDocumentMetadata(
        int id,
        UploadLandDocumentRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
            {
                message = "Metadata-only document registration is not available. Upload the document file.",
            });
        }

        var document = await _landService.UploadDocumentAsync(id, request);
        return Created($"/api/lands/{id}/documents", document);
    }

    /// <summary>
    /// Authenticated download of a stored land document. The file is served as an attachment
    /// with <c>nosniff</c> and no-store headers; the private storage path is never exposed.
    /// </summary>
    [HttpGet("{id}/documents/{documentId}/download")]
    public async Task<IActionResult> DownloadDocument(int id, int documentId)
    {
        var document = await _landService.DownloadDocumentAsync(id, documentId);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(
            document.Content,
            document.ContentType,
            document.FileName,
            enableRangeProcessing: true);
    }

    [HttpGet("{id}/documents")]
    public async Task<ActionResult<List<LandDocumentDto>>> GetDocuments(int id)
    {
        return Ok(await _landService.GetDocumentsAsync(id));
    }

    [HttpPut("{id}/documents/{documentId}/review")]
    public async Task<ActionResult<LandDocumentDto>> ReviewDocument(int id, int documentId, ReviewLandDocumentRequest request)
    {
        return Ok(await _landService.ReviewDocumentAsync(id, documentId, request));
    }

    /// <summary>
    /// SuperAdmin-only technical assessment (region, grid and the four measurements).
    /// Region, grid and the technical measurements are land data: a regular Admin may
    /// review, verify or reject a land but must never modify them, so this endpoint is
    /// restricted to SuperAdmin. Authorization is enforced both here and in the service.
    /// </summary>
    [HttpPut("{id}/assessment")]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<ActionResult<LandDto>> Assess(int id, AssessLandRequest request)
    {
        return Ok(await _landService.AssessAsync(id, request));
    }

    /// <summary>
    /// Assessment history: readable by the land owner and by Admin/SuperAdmin only.
    /// </summary>
    [HttpGet("{id}/assessments")]
    public async Task<ActionResult<List<LandAssessmentDto>>> GetAssessments(int id)
    {
        return Ok(await _landService.GetAssessmentsAsync(id));
    }
}
