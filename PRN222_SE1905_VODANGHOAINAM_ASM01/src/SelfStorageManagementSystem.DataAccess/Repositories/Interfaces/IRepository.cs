using System.Linq.Expressions;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

/// <summary>
/// Generic repository interface for persistence operations over entity types.
/// Note: Keyless views (e.g. UnitTypeHaTDT, StorageUnitHaTDT) do not have primary keys
/// and do not support GetByIdAsync, Update, or Delete.
/// </summary>
/// <typeparam name="T">Entity class type</typeparam>
public interface IRepository<T> where T : class
{
    Task<List<T>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<T?> GetByIdAsync(
        object[] keyValues,
        CancellationToken cancellationToken = default);

    Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        T entity,
        CancellationToken cancellationToken = default);

    void Update(T entity);

    void Delete(T entity);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
