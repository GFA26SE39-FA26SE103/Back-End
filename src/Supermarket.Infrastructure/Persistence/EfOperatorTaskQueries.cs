using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfOperatorTaskQueries(AppDbContext db) : IOperatorTaskQueries
{
    public Task<Guid[]> NeedingAssignmentIds(int limit, CancellationToken ct)
        => db.Tasks.AsNoTracking()
            .Where(task => task.Status == WorkTaskStatus.Offered && task.CurrentAssigneeUserId == null
                && !db.TaskAssignmentHistories.Any(assignment => assignment.TaskId == task.TaskId && assignment.Status == OfferStatus.Offered))
            .OrderByDescending(task => task.CreatedAt)
            .ThenByDescending(task => task.TaskId)
            .Take(limit)
            .Select(task => task.TaskId)
            .ToArrayAsync(ct);
}
