using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

/// <summary>
/// Generic repository implementation providing persistence operations via EF Core.
/// Designed to support entities with single PKs as well as composite PKs via object[] keyValues.
/// Keyless views (e.g. UnitTypeHaTDT, StorageUnitHaTDT) do not support GetByIdAsync, Update, or Delete.
/// </summary>
/// <typeparam name="T">Entity class type</typeparam>
public class GenericRepository<T> : IRepository<T> where T : class
{
    protected readonly SelfStorageDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(SelfStorageDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<T>();
    }

    public virtual async Task<List<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<T?> GetByIdAsync(
        object[] keyValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        return await _dbSet.FindAsync(keyValues, cancellationToken);
    }

    public virtual async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public virtual async Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await _dbSet
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    public virtual async Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return predicate != null
            ? await _dbSet.CountAsync(predicate, cancellationToken)
            : await _dbSet.CountAsync(cancellationToken);
    }

    public virtual async Task AddAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Marks the entity as Modified in EF Core ChangeTracker.
    /// Generic Update(T entity) assumes the caller owns a complete entity state.
    /// Feature Services should normally prefer loading an existing tracked entity,
    /// validating business rules, modifying permitted properties, and calling SaveChangesAsync,
    /// rather than creating a partial entity and updating all properties indiscriminately.
    /// </summary>
    public virtual void Update(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _dbSet.Update(entity);
    }

    public virtual void Delete(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _dbSet.Remove(entity);
    }

    public virtual async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
