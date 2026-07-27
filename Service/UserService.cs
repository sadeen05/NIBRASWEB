using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class UserService : IUserService
{
    private readonly IRepository<User> _userRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IRepository<User> userRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        _logger.LogInformation("Get all users");

        var users = await _userRepository.GetAllAsync();
        var result = new List<UserDto>();

        foreach (var user in users)
        {
            result.Add(user.Adapt<UserDto>());
        }

        return result;
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);

        if (user == null)
        {
            _logger.LogWarning("User not found");
            return null;
        }

        return user.Adapt<UserDto>();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        var user = request.Adapt<User>();
        user.PasswordHash = "default_pass";

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return user.Adapt<UserDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id);

        if (user == null)
        {
            _logger.LogWarning("User not found for update");
            return false;
        }

        user.FullName = request.FullName;
        user.Email = request.Email;
        user.Phone = request.Phone;

        await _userRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _userRepository.DeleteAsync(id);

        if (result == false)
        {
            _logger.LogWarning("User not found for delete");
            return false;
        }

        await _userRepository.SaveChangesAsync();
        return true;
    }
}
