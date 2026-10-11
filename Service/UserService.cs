using Microsoft.EntityFrameworkCore;
using NIBRAS;
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
            .Where(u => !u.IsDeleted)
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
        var user = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            _logger.LogWarning("User not found");
            return null;
        }
        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email && !u.IsDeleted);
        if (emailExists)
            throw new BusinessRuleException("Email already exists.");

        if (request.RoleId is < 1 or > 3)
            throw new ValidationFailedException("User role is invalid.");

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            RoleId = request.RoleId,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            IsDeleted = false
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return ToDto(user);
    }

    public async Task<bool> ApproveAsync(int id, int approvedById)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            return false;
        }

        if (user.Role.Name != RoleNames.Investor ||
            string.IsNullOrWhiteSpace(user.CompanyName) ||
            string.IsNullOrWhiteSpace(user.CompanyRegistrationNumber))
        {
            throw new ValidationFailedException("Only an Investor with company details can be approved.");
        }

        user.IsApproved = true;
        user.ApprovedById = approvedById;
        user.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null)
        {
            _logger.LogWarning("User not found for update");
            return false;
        }

        if (user.Email != request.Email)
        {
            var emailExists = await _context.Users.AnyAsync(u =>
                u.Email == request.Email && u.Id != id && !u.IsDeleted);
            if (emailExists)
                throw new BusinessRuleException("Email already in use by another user.");
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
            throw new BusinessRuleException("Cannot delete user who owns lands.");

        var hasContracts = await _context.Contracts.AnyAsync(c => (c.InvestorId == id || c.LandlordId == id));
        if (hasContracts)
            throw new BusinessRuleException("Cannot delete user who has contracts.");

        var hasPendingCancellations = await _context.Contracts.AnyAsync(c =>
            (c.LandlordId == id || c.InvestorId == id)
            && c.CancellationRequestedById != null
            && (c.CancellationEffectiveDate >= DateTime.UtcNow || c.DisputeFlagged));
        if (hasPendingCancellations)
            throw new BusinessRuleException("Cannot delete user with pending contract cancellations.");

        var user = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
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

    private static UserDto ToDto(User user) =>
        new(user.Id, user.FullName, user.Email, user.Phone, user.Role?.Name ?? string.Empty, user.CreatedAt);
}