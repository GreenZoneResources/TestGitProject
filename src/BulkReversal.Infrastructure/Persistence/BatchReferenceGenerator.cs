using BulkReversal.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence;

/// <summary>
/// Produces references shaped BR-yyyyMMdd-NN (e.g. BR-20260805-01), matching the samples in the
/// BRD's figma mockups. NN is the count of batches already created today, so a collision can only
/// occur under true concurrent uploads on the same second — at Settlement-team upload volumes this
/// is acceptable, and the unique index on BatchReference guarantees any collision surfaces as a
/// clear SaveChanges failure rather than silent data corruption.
/// </summary>
public class BatchReferenceGenerator : IBatchReferenceGenerator
{
    private readonly BulkReversalDbContext _db;

    public BatchReferenceGenerator(BulkReversalDbContext db) => _db = db;

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var today = DateTimeOffset.UtcNow;
        var datePart = today.ToString("yyyyMMdd");
        var prefix = $"BR-{datePart}-";

        var todayStart = new DateTimeOffset(today.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        var countToday = await _db.ReversalBatches
            .Where(b => b.UploadedAt >= todayStart && b.UploadedAt < todayEnd)
            .CountAsync(ct);

        for (var attempt = countToday + 1; attempt < countToday + 1000; attempt++)
        {
            var candidate = $"{prefix}{attempt:D2}";
            var exists = await _db.ReversalBatches.AnyAsync(b => b.BatchReference == candidate, ct);
            if (!exists) return candidate;
        }

        // Astronomically unlikely fallback: fall back to a fully unique suffix.
        return $"{prefix}{Guid.NewGuid():N}"[..30];
    }
}
