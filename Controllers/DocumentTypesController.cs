using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
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
        var types = await _documentTypeService.GetAllAsync();
        return Ok(types);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DocumentTypeDto>> GetById(int id)
    {
        var type = await _documentTypeService.GetByIdAsync(id);

        if (type == null)
        {
            return NotFound();
        }

        return Ok(type);
    }

    [HttpPost]
    public async Task<ActionResult<DocumentTypeDto>> Create(CreateDocumentTypeRequest request)
    {
        var type = await _documentTypeService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = type.Id }, type);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateDocumentTypeRequest request)
    {
        var result = await _documentTypeService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _documentTypeService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
