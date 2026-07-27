using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandDocumentsController : ControllerBase
{
    private readonly ILandDocumentService _landDocumentService;

    public LandDocumentsController(ILandDocumentService landDocumentService)
    {
        _landDocumentService = landDocumentService;
    }

    [HttpGet]
    public async Task<ActionResult<List<LandDocumentDto>>> GetAll()
    {
        var documents = await _landDocumentService.GetAllAsync();
        return Ok(documents);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandDocumentDto>> GetById(int id)
    {
        var document = await _landDocumentService.GetByIdAsync(id);

        if (document == null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    [HttpPost]
    public async Task<ActionResult<LandDocumentDto>> Create(CreateLandDocumentRequest request)
    {
        var document = await _landDocumentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandDocumentRequest request)
    {
        var result = await _landDocumentService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _landDocumentService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
