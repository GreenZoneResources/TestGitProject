using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Features.Approvals.Dtos;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BulkReversal.Application.Features.Approvals;

public class ApprovalService : IApprovalService
{
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        IReversalBatchRepository batchRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<ApprovalService> logger)
    {
        _batchRepository = batchRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PagedResult<ApprovalListItemDto>> SearchPendingAsync(string? batchReferenceContains, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _batchRepository.SearchAsync(
            batchReferenceContains, BatchStatus.PendingApproval, null, null, page, pageSize, ct);

        var dtos = items.Select(b => new ApprovalListItemDto(
            b.Id, b.BatchReference, b.SubmittedByName ?? b.UploadedByName, b.ValidRecords, b.SubmittedAt)).ToList();

        return PagedResult<ApprovalListItemDto>.Create(dtos, page, pageSize, total);
    }

    public async Task<ApprovalDetailDto> GetDetailAsync(string batchReference, int page, int pageSize, CancellationToken ct = default)
    {
        var batch = await _batchRepository.GetByReferenceAsync(batchReference, includeTransactions: true, ct)
            ?? throw new NotFoundAppException(nameof(ReversalBatch), batchReference);

        var validRows = batch.Transactions
            .Where(t => t.RowValidationStatus == RowValidationStatus.Valid)
            .OrderBy(t => t.RowNumber)
            .ToList();

        var pageItems = validRows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ApprovalTransactionRowDto(
                t.RowNumber, t.TransactionType, t.SessionIdOrFtReference, t.Rrn, t.AccountNumber,
                t.TransactionAmount, t.BeneficiaryBank, t.Biller))
            .ToList();

        return new ApprovalDetailDto(
            batch.Id, batch.BatchReference, batch.SubmittedByName ?? batch.UploadedByName, batch.SubmittedAt,
            batch.ValidRecords, batch.Status, pageItems, page, pageSize, validRows.Count);
    }

    public async Task ApproveAsync(string batchReference, CancellationToken ct = default)
    {
        var batch = await _batchRepository.GetByReferenceAsync(batchReference, includeTransactions: true, ct)
            ?? throw new NotFoundAppException(nameof(ReversalBatch), batchReference);

        batch.Approve(_currentUser.UserId, _currentUser.UserName);

        _auditService.Record(AuditAction.Approval, nameof(ReversalBatch), batch.Id.ToString(), batch.BatchReference,
            $"Batch {batch.BatchReference} approved: {batch.ValidRecords} record(s) released to the reversal engine.");

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Batch {BatchReference} approved by {UserId}.", batch.BatchReference, _currentUser.UserId);
    }

    public async Task RejectAsync(string batchReference, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException(nameof(reason), "A rejection reason is required.");

        var batch = await _batchRepository.GetByReferenceAsync(batchReference, includeTransactions: false, ct)
            ?? throw new NotFoundAppException(nameof(ReversalBatch), batchReference);

        batch.Reject(_currentUser.UserId, _currentUser.UserName, reason);

        _auditService.Record(AuditAction.Rejection, nameof(ReversalBatch), batch.Id.ToString(), batch.BatchReference,
            $"Batch {batch.BatchReference} rejected: {reason}");

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Batch {BatchReference} rejected by {UserId}.", batch.BatchReference, _currentUser.UserId);
    }
}
