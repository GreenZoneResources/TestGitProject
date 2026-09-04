using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence;

public class BulkReversalDbContext : DbContext
{
    public BulkReversalDbContext(DbContextOptions<BulkReversalDbContext> options) : base(options) { }

    public DbSet<ReversalBatch> ReversalBatches => Set<ReversalBatch>();
    public DbSet<ReversalTransaction> ReversalTransactions => Set<ReversalTransaction>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BulkReversalDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
