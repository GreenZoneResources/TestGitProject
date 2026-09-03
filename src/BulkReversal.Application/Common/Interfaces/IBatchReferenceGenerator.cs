namespace BulkReversal.Application.Common.Interfaces;

/// <summary>Generates unique Batch Reference Numbers, e.g. BR-20260805-01 (FR-09).</summary>
public interface IBatchReferenceGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}
