using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class UserService : IUserService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(NebrasdbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        _logger.LogInformation("Get all users");

        var users = await _context.Users.ToListAsync();
        var result = new List<UserDto>();
        foreach (var user in users) result.Add(user.Adapt<UserDto>());
        return result;
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            _logger.LogWarning("User not found");
            return null;
        }
        return user.Adapt<UserDto>();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        // البريد الإلكتروني فريد
        var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email);
        if (emailExists)
            throw new InvalidOperationException("Email already exists.");

        var user = request.Adapt<User>();
        user.PasswordHash = "default_pass";

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user.Adapt<UserDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            _logger.LogWarning("User not found for update");
            return false;
        }

        // لو تغير الإيميل → تأكد إنه فريد
        if (user.Email != request.Email)
        {
            var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email && u.Id != id);
            if (emailExists)
                throw new InvalidOperationException("Email already in use by another user.");
        }

        user.FullName = request.FullName;
        user.Email = request.Email;
        user.Phone = request.Phone;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // ما ينحذف وعنده Land أو Contract
        var hasLands = await _context.Lands.AnyAsync(l => l.LandlordId == id && !l.IsDeleted);
        if (hasLands)
            throw new InvalidOperationException("Cannot delete user who owns lands.");

        var hasContracts = await _context.Contracts.AnyAsync(c => c.InvestorId == id || c.LandlordId == id);
        if (hasContracts)
            throw new InvalidOperationException("Cannot delete user who has contracts.");

        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            _logger.LogWarning("User not found for delete");
            return false;
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }
}
