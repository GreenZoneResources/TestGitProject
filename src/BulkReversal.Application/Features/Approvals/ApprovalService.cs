using System.Net;
using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Approvals.Dtos;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Approvals;

public class ApprovalService : IApprovalService
{
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IReversalTransactionRepository _transactionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IUserContactDirectoryRepository _contactDirectory;
    private readonly IEmailService _emailService;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        IReversalBatchRepository batchRepository,
        IReversalTransactionRepository transactionRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IUserContactDirectoryRepository contactDirectory,
        IEmailService emailService,
        IOptions<EmailOptions> emailOptions,
        ILogger<ApprovalService> logger)
    {
        _batchRepository = batchRepository;
        _transactionRepository = transactionRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _auditService = auditService;
        _contactDirectory = contactDirectory;
        _emailService = emailService;
        _emailOptions = emailOptions.Value;
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

        // BRU-04, re-checked immediately before release: a reference in this batch may have become
        // active elsewhere (another batch approved, or independently reversed) in the time between
        // this batch's own validation and now. Approving anyway would let the same source
        // transaction be reversed twice.
        var candidateRefs = batch.Transactions
            .Where(t => t.RowValidationStatus == RowValidationStatus.Valid)
            .Select(t => t.SessionIdOrFtReference);
        var conflicts = await _transactionRepository.GetActiveConflictReferencesAsync(candidateRefs, excludeBatchId: batch.Id, ct: ct);
        if (conflicts.Count > 0)
        {
            throw new ConflictAppException(
                $"Batch {batch.BatchReference} cannot be approved: reference(s) {string.Join(", ", conflicts)} " +
                "are already reversed or active in another batch. Remove or correct the affected row(s) and resubmit.");
        }

        batch.Approve(_currentUser.UserId, _currentUser.UserName);

        _auditService.Record(AuditAction.Approval, nameof(ReversalBatch), batch.Id.ToString(), batch.BatchReference,
            $"Batch {batch.BatchReference} approved: {batch.ValidRecords} record(s) released to the reversal engine.");

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Batch {BatchReference} approved by {UserId}.", batch.BatchReference, _currentUser.UserId);

        await NotifyInitiatorOfDecisionAsync(batch, approved: true, reason: null, ct);
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

        await NotifyInitiatorOfDecisionAsync(batch, approved: false, reason, ct);
    }

    /// <summary>Notifies whoever submitted the batch for approval — best-effort, so a delivery
    /// failure here must never undo the decision that already succeeded above.</summary>
    private async Task NotifyInitiatorOfDecisionAsync(ReversalBatch batch, bool approved, string? reason, CancellationToken ct)
    {
        var initiatorUserId = batch.SubmittedByUserId ?? batch.UploadedByUserId;
        var initiatorEmail = await _contactDirectory.GetEmailForUserAsync(initiatorUserId, ct);
        if (string.IsNullOrWhiteSpace(initiatorEmail))
        {
            _logger.LogWarning("No email on file for {UserId}; skipping decision notification for batch {BatchReference}.", initiatorUserId, batch.BatchReference);
            return;
        }

        var link = string.IsNullOrWhiteSpace(_emailOptions.PortalBaseUrl)
            ? null
            : $"{_emailOptions.PortalBaseUrl!.TrimEnd('/')}/status-monitoring?batchReference={batch.BatchReference}";

        var outcome = approved ? "approved" : "rejected";
        var body =
            $"<p>Batch <b>{Encode(batch.BatchReference)}</b> ('{Encode(batch.BatchName)}') was {outcome} by {Encode(_currentUser.UserName)}.</p>" +
            (approved ? "" : $"<p>Reason: {Encode(reason ?? string.Empty)}</p>") +
            (link is null ? "" : $"<p><a href=\"{link}\">Open in the Settlement Portal</a></p>");

        await _emailService.SendAsync([initiatorEmail], $"Reversal batch {batch.BatchReference} {outcome}", body, ct);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
