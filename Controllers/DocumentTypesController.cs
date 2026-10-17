using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/document-types")]
[Authorize]
public class DocumentTypesController : ControllerBase
{
    private readonly IDocumentTypeService _documentTypeService;

    public DocumentTypesController(IDocumentTypeService documentTypeService)
    {
        _documentTypeService = documentTypeService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentTypeDto>>> GetAll()
    {
        return Ok(await _documentTypeService.GetAllAsync());
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
    public async Task<ActionResult<DocumentTypeDto>> Create(DocumentTypeRequest request)
    {
        var documentType = await _documentTypeService.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, documentType);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
    public async Task<ActionResult<DocumentTypeDto>> Update(int id, DocumentTypeRequest request)
    {
        return Ok(await _documentTypeService.UpdateAsync(id, request));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        await _documentTypeService.DeleteAsync(id);
        return NoContent();
    }
}
