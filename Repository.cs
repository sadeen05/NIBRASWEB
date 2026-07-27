using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;

namespace NIBRAS;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly NebrasdbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(NebrasdbContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    public async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();

    public async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);

    public async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _dbSet.FindAsync(id);
        if (entity == null) return false;
        _dbSet.Remove(entity);
        return true;
    }

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
