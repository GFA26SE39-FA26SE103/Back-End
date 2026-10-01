using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Entity = Supermarket.Domain.Entity;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Infrastructure.Persistence;

public sealed class EfSetupStore(AppDbContext db, IClock clock) : ISetupStore
{
    private ISet<T> Set<T>() where T : Entity, new()
    {
        var persistenceType = typeof(AppDbContext).Assembly.GetType($"Supermarket.Infrastructure.Persistence.Scaffolded.{typeof(T).Name}")
            ?? throw new InvalidOperationException("Unsupported persistence entity.");
        return (ISet<T>)Activator.CreateInstance(typeof(EfSet<,>).MakeGenericType(typeof(T), persistenceType), db, clock)!;
    }
    public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => Set<T>().Find(id, ct);
    public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() => Set<T>().List(filter, ct);
    public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Set<T>().Add(entity, ct);
    public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Set<T>().Update(entity, false, ct);
    public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Set<T>().Update(entity, true, ct);
    public async Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default)
    {
        // Range locks protect last-Admin checks, the single branch and health-event deduplication.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await work();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw new AppError("CONCURRENT_UPDATE", "The resource changed; reload and retry."); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 547)
        {
            throw new AppError("DATABASE_CONFLICT", "The request conflicts with an existing resource or relationship.");
        }
        catch (Exception e) when (SqlNumber(e) == 1205) { throw new AppError("CONCURRENT_UPDATE", "A concurrent operation conflicted; retry."); }
        finally { db.ChangeTracker.Clear(); }
    }
    private static int? SqlNumber(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql)
                return sql.Number;
        return null;
    }
    private interface ISet<T> where T : Entity, new()
    {
        Task<T?> Find(Guid id, CancellationToken ct);
        Task<List<T>> List(Expression<Func<T, bool>>? filter, CancellationToken ct);
        Task Add(T entity, CancellationToken ct);
        Task Update(T entity, bool remove, CancellationToken ct);
    }
    private sealed class EfSet<TDomain, TDb>(AppDbContext db, IClock clock) : ISet<TDomain> where TDomain : Entity, new() where TDb : class, new()
    {
        private static readonly (PropertyInfo Domain, PropertyInfo Db)[] Properties = typeof(TDomain).GetProperties().Select(p => (p, typeof(TDb).GetProperty(p.Name)!)).ToArray();
        private static TDomain ToDomain(TDb entity)
        {
            var result = new TDomain();
            foreach (var (domain, persistence) in Properties)
            {
                var value = persistence.GetValue(entity);
                if (value is DateTime date)
                    value = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                domain.SetValue(result, value);
            }
            return result;
        }
        private static void Copy(TDomain domain, TDb entity)
        {
            foreach (var (d, p) in Properties)
                p.SetValue(entity, d.GetValue(domain));
        }
        public async Task<TDomain?> Find(Guid id, CancellationToken ct)
        {
            var entity = await db.Set<TDb>().FindAsync([id], ct);
            return entity is null ? null : ToDomain(entity);
        }
        public async Task<List<TDomain>> List(Expression<Func<TDomain, bool>>? filter, CancellationToken ct)
        {
            IQueryable<TDb> query = db.Set<TDb>().AsNoTracking();
            if (filter is not null)
            {
                var parameter = Expression.Parameter(typeof(TDb), "entity");
                var body = new PredicateMapper(filter.Parameters[0], parameter).Visit(filter.Body)!;
                query = query.Where(Expression.Lambda<Func<TDb, bool>>(body, parameter));
            }
            return (await query.ToListAsync(ct)).Select(ToDomain).ToList();
        }
        public async Task Add(TDomain entity, CancellationToken ct)
        {
            if (entity is TrackedEntity tracked)
                tracked.CreatedAt = tracked.UpdatedAt = Timestamp(clock.UtcNow);
            var row = new TDb();
            Copy(entity, row);
            await db.Set<TDb>().AddAsync(row, ct);
        }
        public async Task Update(TDomain entity, bool remove, CancellationToken ct)
        {
            var key = db.Model.FindEntityType(typeof(TDb))!.FindPrimaryKey()!.Properties.Single().Name;
            var id = typeof(TDomain).GetProperty(key)!.GetValue(entity)!;
            var row = await db.Set<TDb>().FindAsync([id], ct) ?? throw new AppError("NOT_FOUND", "The resource was not found.", 404);
            if (entity is TrackedEntity tracked)
            {
                var entry = db.Entry(row);
                entry.Property("UpdatedAt").OriginalValue = tracked.UpdatedAt;
                tracked.UpdatedAt = Timestamp(clock.UtcNow);
                if (tracked.UpdatedAt <= (DateTime)entry.Property("UpdatedAt").OriginalValue!)
                    tracked.UpdatedAt = ((DateTime)entry.Property("UpdatedAt").OriginalValue!).AddMilliseconds(1);
            }
            if (remove)
                db.Set<TDb>().Remove(row);
            else
                Copy(entity, row);
        }
        private static DateTime Timestamp(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
    private sealed class PredicateMapper(ParameterExpression original, ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == original ? replacement : base.VisitParameter(node);
        protected override Expression VisitMember(MemberExpression node) => node.Expression == original ? Expression.Property(replacement, node.Member.Name) : base.VisitMember(node);
    }
}
