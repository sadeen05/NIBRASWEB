using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IAuthService
{
    Task<AuthUserSummary> RegisterAsync(RegisterRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthUserSummary?> GetCurrentUserAsync(int id);
}
