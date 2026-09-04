using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence.Repositories;

public class UserRoleAssignmentRepository : IUserRoleAssignmentRepository
{
    private readonly BulkReversalDbContext _db;

    public UserRoleAssignmentRepository(BulkReversalDbContext db) => _db = db;

    public Task<UserRoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.UserRoleAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> HasActiveAssignmentAsync(string userId, string role, CancellationToken ct = default) =>
        _db.UserRoleAssignments.AnyAsync(a => a.UserId == userId && a.Role == role && a.IsActive, ct);

    public async Task<IReadOnlyList<string>> GetActiveRoleNamesAsync(string userId, CancellationToken ct = default) =>
        await _db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => a.Role)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetActiveEmailsByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default)
    {
        var roleList = roles.Distinct().ToList();
        if (roleList.Count == 0) return [];

        return await _db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.IsActive && roleList.Contains(a.Role))
            .Select(a => a.Email)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<string?> GetActiveEmailForUserAsync(string userId, CancellationToken ct = default) =>
        await _db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UserRoleAssignment>> ListAsync(CancellationToken ct = default) =>
        await _db.UserRoleAssignments
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public void Add(UserRoleAssignment assignment) => _db.UserRoleAssignments.Add(assignment);
}
