using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto> CreateAsync(CreateUserRequest request);
    Task<bool> UpdateAsync(int id, UpdateUserRequest request);
    Task<bool> DeleteAsync(int id);
}
