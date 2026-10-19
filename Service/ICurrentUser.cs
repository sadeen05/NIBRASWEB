namespace NIBRAS.API.Services;

public interface ICurrentUser
{
    int? Id { get; }
    string? Role { get; }

    /// <summary>The remote address of the request, for the audit trail. Never a credential.</summary>
    string? Ip { get; }
}
