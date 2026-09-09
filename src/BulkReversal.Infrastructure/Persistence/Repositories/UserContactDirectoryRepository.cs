using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence.Repositories;

public class UserContactDirectoryRepository : IUserContactDirectoryRepository
{
    private readonly BulkReversalDbContext _db;

    public UserContactDirectoryRepository(BulkReversalDbContext db) => _db = db;

    public async Task SyncFromSignInAsync(string userId, string userName, string email, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var existing = await _db.UserContactDirectory
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);

        foreach (var role in roles)
        {
            var entry = existing.FirstOrDefault(e => e.Role == role);
            if (entry is null)
            {
                _db.UserContactDirectory.Add(UserContactDirectoryEntry.Create(userId, userName, email, role));
            }
            else
            {
                entry.RefreshFromSignIn(userName, email);
            }
        }

        foreach (var stale in existing.Where(e => e.IsCurrentlyHeld && !roles.Contains(e.Role)))
        {
            stale.MarkNoLongerHeld();
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetEmailsByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default)
    {
        var roleList = roles.Distinct().ToList();
        if (roleList.Count == 0) return [];

        return await _db.UserContactDirectory
            .AsNoTracking()
            .Where(e => e.IsCurrentlyHeld && roleList.Contains(e.Role))
            .Select(e => e.Email)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<string?> GetEmailForUserAsync(string userId, CancellationToken ct = default) =>
        await _db.UserContactDirectory
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.LastSeenAt)
            .Select(e => e.Email)
            .FirstOrDefaultAsync(ct);
}
