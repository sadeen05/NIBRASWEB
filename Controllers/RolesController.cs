using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> GetAll()
    {
        var roles = await _roleService.GetAllAsync();
        return Ok(roles);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDto>> GetById(int id)
    {
        var role = await _roleService.GetByIdAsync(id);

        if (role == null)
        {
            return NotFound();
        }

        return Ok(role);
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request)
    {
        var role = await _roleService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = role.Id }, role);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateRoleRequest request)
    {
        var result = await _roleService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _roleService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
