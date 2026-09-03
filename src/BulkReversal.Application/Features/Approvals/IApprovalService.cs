using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Features.Approvals.Dtos;

namespace BulkReversal.Application.Features.Approvals;

/// <summary>Backs the Approvals screen (BRD figma): review and action reversal batches
/// awaiting sign-off (FR-09, section 4 "Portal Login" .. "System Revalidation").</summary>
public interface IApprovalService
{
    Task<PagedResult<ApprovalListItemDto>> SearchPendingAsync(string? batchReferenceContains, int page, int pageSize, CancellationToken ct = default);

    Task<ApprovalDetailDto> GetDetailAsync(string batchReference, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Authorizes the batch: valid rows become eligible for pickup by the reversal engine (FR-10).</summary>
    Task ApproveAsync(string batchReference, CancellationToken ct = default);

    Task RejectAsync(string batchReference, string reason, CancellationToken ct = default);
}
