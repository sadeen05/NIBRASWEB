namespace NIBRAS.API.Services;

public interface ICurrentUser
{
    int? Id { get; }
    string? Role { get; }
}
