using System.Security.Claims;

namespace NIBRAS.API.Services;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? Id
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Role =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
}
