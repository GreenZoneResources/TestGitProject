using BulkReversal.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly BulkReversalDbContext _db;

    public UnitOfWork(BulkReversalDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await operation(ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
