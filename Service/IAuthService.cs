using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IAuthService
{
    Task<AuthUserSummary> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Signs an account in. The answer is null when the address or the password is wrong, and
    /// the request is refused with a 403 when the address of the account has not been
    /// confirmed yet.
    /// </summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthUserSummary?> GetCurrentUserAsync(int id);
}
