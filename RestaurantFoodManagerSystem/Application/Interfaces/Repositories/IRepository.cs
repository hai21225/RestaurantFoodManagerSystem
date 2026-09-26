using System.Linq.Expressions;

public interface IRepository<T> where T : class
{
    public Task<T?> GetByIdAsync(object id);

    public Task<List<T>> GetAllAsync();

    public Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate);

    public Task<T?> FindOneAsync(
        Expression<Func<T, bool>> predicate);

    public Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate);

    public Task AddAsync(T entity);

    public void Update(T entity);

    public void Delete(T entity);
}
