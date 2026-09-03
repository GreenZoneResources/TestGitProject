using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.Provider.Dtos;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Provider;

/// <summary>
/// Implements the provider side of provider-integration-contract.md. BulkReversal.API is polled
/// by SingleReversalEngine.Orchestrator (§1) and later receives its outcome callback (§2).
/// </summary>
public class ProviderIntegrationService : IProviderIntegrationService
{
    private readonly IReversalTransactionRepository _transactionRepository;
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ProviderIntegrationOptions _options;
    private readonly ILogger<ProviderIntegrationService> _logger;

    public ProviderIntegrationService(
        IReversalTransactionRepository transactionRepository,
        IReversalBatchRepository batchRepository,
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        IOptions<ProviderIntegrationOptions> options,
        ILogger<ProviderIntegrationService> logger)
    {
        _transactionRepository = transactionRepository;
        _batchRepository = batchRepository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PendingReversalItemDto>> GetPendingReversalsAsync(int maxItems, CancellationToken ct = default)
    {
        var take = maxItems <= 0 ? _options.MaxItemsPerPoll : Math.Min(maxItems, _options.MaxItemsPerPoll);
        var pending = await _transactionRepository.GetPendingForEngineAsync(take, ct);

        if (pending.Count == 0)
        {
            return [];
        }

        var items = new List<PendingReversalItemDto>(pending.Count);
        foreach (var txn in pending)
        {
            // §1.4: keep returning the item on every poll until the callback is received; the first
            // pickup flips it from Submitted -> Processing so Status Monitoring reflects reality.
            if (txn.Status == ReversalStatus.Submitted)
            {
                txn.MarkRetrievedByEngine();
            }

            items.Add(new PendingReversalItemDto
            {
                MsgId = txn.MsgId,
                Amount = txn.TransactionAmount,
                Currency = _options.Currency,
                DebtorAccountNumber = txn.AccountNumber,
                FtReference = txn.SessionIdOrFtReference
            });
        }

        _auditService.Record(
            AuditAction.ApiRetrieval,
            nameof(ReversalTransaction),
            entityId: "batch",
            batchReference: null,
            details: $"Reversal engine retrieved {items.Count} pending item(s).");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Reversal engine polled {Count} pending item(s).", items.Count);
        return items;
    }

    public async Task<CallbackOutcome> ApplyCallbackAsync(ReversalCallbackRequest request, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(request.MsgId, "N", out var transactionId))
        {
            _logger.LogWarning("Reversal callback received with malformed msgId '{MsgId}'.", request.MsgId);
            return CallbackOutcome.MalformedMsgId;
        }

        var transaction = await _transactionRepository.GetByIdAsync(transactionId, ct);
        if (transaction is null)
        {
            _logger.LogWarning("Reversal callback received for unknown msgId '{MsgId}'.", request.MsgId);
            return CallbackOutcome.UnknownMsgId;
        }

        var wasAlreadyTerminal = transaction.Status is ReversalStatus.Reversed or ReversalStatus.Rejected;

        transaction.ApplyCallback(request.IsSuccessful, request.ExternalReference, request.FailureCode, request.FailureReason);

        if (!wasAlreadyTerminal)
        {
            _auditService.Record(
                AuditAction.ProcessingOutcome,
                nameof(ReversalTransaction),
                transaction.Id.ToString(),
                transaction.BatchReference,
                request.IsSuccessful
                    ? $"Reversal succeeded for {transaction.SessionIdOrFtReference} (external ref: {request.ExternalReference})."
                    : $"Reversal failed for {transaction.SessionIdOrFtReference}: [{request.FailureCode}] {request.FailureReason}");

            var batch = await _batchRepository.GetByIdAsync(transaction.BatchId, includeTransactions: true, ct);
            batch?.RefreshCompletionState();

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Callback applied for {FtReference} (msgId {MsgId}): success={Success}.",
                transaction.SessionIdOrFtReference, request.MsgId, request.IsSuccessful);
        }
        else
        {
            _logger.LogInformation("Duplicate callback for already-terminal msgId '{MsgId}' ignored (no-op).", request.MsgId);
        }

        return CallbackOutcome.Applied;
    }
}
