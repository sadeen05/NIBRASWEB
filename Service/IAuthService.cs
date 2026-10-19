using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IAuthService
{
    /// <summary>
    /// Creates a new Landlord or Investor account. The account is unverified: it has to confirm
    /// its address before it may be used, and the answer carries the status of the
    /// verification.
    /// </summary>
    Task<AuthUserSummary> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs an account in. The answer is null when the address or the password is wrong, and
    /// the request is refused with a 403 when the address of the account has not been
    /// confirmed yet.
    /// </summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthUserSummary?> GetCurrentUserAsync(int id);
}
