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

        var pagesize = 10;
        var pageIndex = 0;
        var users = await _context.Users
            .Skip(pageIndex * pagesize)
            .Take(pagesize)
            .ToListAsync();

        var userDtos = users.Select(u => new UserDto(
         u.Id,
         u.FullName,
         u.Email,
         u.Phone,
         u.Role != null ? u.Role.Name : string.Empty,
         u.CreatedAt
     )).ToList();

        return userDtos;

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
        var hasLands = await _context.Lands.AnyAsync(l => l.LandlordId == id && !l.IsDeleted);
        if (hasLands)
            throw new InvalidOperationException("Cannot delete user who owns lands.");

        var hasContracts = await _context.Contracts.AnyAsync(c => (c.InvestorId == id || c.LandlordId == id));
        if (hasContracts)
            throw new InvalidOperationException("Cannot delete user who has contracts.");

        var hasPendingCancellations = await _context.Contracts.AnyAsync(c =>
            (c.LandlordId == id || c.InvestorId == id)
            && c.CancellationRequestedById != null
            && (c.CancellationEffectiveDate >= DateTime.UtcNow || c.DisputeFlagged));
        if (hasPendingCancellations)
            throw new InvalidOperationException("Cannot delete user with pending contract cancellations.");

        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            _logger.LogWarning("User not found or already deleted");
            return false;
        }

        user.IsDeleted = true;

        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return true;
    }
}