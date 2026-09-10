using System.Net;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Upload.Services;

/// <summary>
/// Orchestrates FR-03 through FR-09: validates the submitted batch (field-level, cross-row, and
/// cross-system rules from Section 5), stages it, assigns its Batch Reference Number, and lets
/// Settlement correct individual rows (edit/delete/retry) before explicitly submitting for approval.
/// </summary>
public class BatchUploadService : IBatchUploadService
{
    private readonly UploadRowFieldValidator _fieldValidator;
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IReversalTransactionRepository _transactionRepository;
    private readonly IBatchReferenceGenerator _referenceGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ITransferService _transferService;
    private readonly IUserContactDirectoryRepository _contactDirectory;
    private readonly IEmailService _emailService;
    private readonly BusinessRulesOptions _rules;
    private readonly TransferServiceOptions _transferOptions;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<BatchUploadService> _logger;

    public BatchUploadService(
        UploadRowFieldValidator fieldValidator,
        IReversalBatchRepository batchRepository,
        IReversalTransactionRepository transactionRepository,
        IBatchReferenceGenerator referenceGenerator,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ITransferService transferService,
        IUserContactDirectoryRepository contactDirectory,
        IEmailService emailService,
        IOptions<BusinessRulesOptions> rules,
        IOptions<TransferServiceOptions> transferOptions,
        IOptions<EmailOptions> emailOptions,
        ILogger<BatchUploadService> logger)
    {
        _fieldValidator = fieldValidator;
        _batchRepository = batchRepository;
        _transactionRepository = transactionRepository;
        _referenceGenerator = referenceGenerator;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _auditService = auditService;
        _transferService = transferService;
        _contactDirectory = contactDirectory;
        _emailService = emailService;
        _rules = rules.Value;
        _transferOptions = transferOptions.Value;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task<UploadBatchResultDto> CreateBatchAsync(CreateReversalBatchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BatchName))
            throw new ValidationAppException(nameof(request.BatchName), "Batch name is required.");

        if (request.Transactions.Count == 0)
            throw new ValidationAppException(nameof(request.Transactions), "At least one transaction is required.");

        if (request.Transactions.Count > _rules.MaxRecordsPerFile) // BRU-01
            throw new ValidationAppException(nameof(request.Transactions), $"{request.Transactions.Count} records were submitted, exceeding the maximum of {_rules.MaxRecordsPerFile} records per batch.");

        // BRU-06: duplicate references within the same submission.
        var duplicateRefsInBatch = request.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.SessionIdOrFtReference))
            .GroupBy(t => t.SessionIdOrFtReference, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // BRU-04: references with an active reversal elsewhere in the system — already reversed,
        // already released to the engine, or still a live valid row staged in another batch.
        var candidateRefs = request.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.SessionIdOrFtReference))
            .Select(t => t.SessionIdOrFtReference)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var conflictingRefs = await _transactionRepository.GetActiveConflictReferencesAsync(candidateRefs, ct: ct);

        // BRU-05: source-transaction confirmation, bounded-concurrency so a large batch doesn't
        // open hundreds of simultaneous connections to the Transfer Service.
        var sourceCheckErrors = await CheckSourceTransactionsAsync(request.Transactions, ct);

        // Validate every row up front, before anything touches the database. A single invalid or
        // duplicate row rejects the whole batch (400) — nothing is persisted unless every row
        // passes. The caller fixes their source data and resubmits the entire batch; there is no
        // partially-valid batch left staged in the database to correct row-by-row.
        var rowErrors = new Dictionary<string, string[]>();
        for (var i = 0; i < request.Transactions.Count; i++)
        {
            var row = request.Transactions[i];
            var errors = _fieldValidator.Validate(row);
            errors.AddRange(sourceCheckErrors[i]);

            if (!string.IsNullOrWhiteSpace(row.SessionIdOrFtReference))
            {
                if (duplicateRefsInBatch.Contains(row.SessionIdOrFtReference))
                    errors.Add($"Session ID / FT Reference '{row.SessionIdOrFtReference}' appears more than once in this batch.");

                if (conflictingRefs.Contains(row.SessionIdOrFtReference))
                    errors.Add($"Session ID / FT Reference '{row.SessionIdOrFtReference}' is already reversed or already active in another batch.");
            }

            if (errors.Count > 0)
                rowErrors[$"transactions[{i}]"] = errors.ToArray();
        }

        if (rowErrors.Count > 0)
            throw new ValidationAppException(rowErrors);

        // Every row passed — safe to persist. Every row is Valid by construction; an invalid row
        // never reaches storage.
        var batchReference = await _referenceGenerator.NextAsync(ct);
        var batch = ReversalBatch.Create(request.BatchName, _currentUser.UserId, _currentUser.UserName, batchReference);

        for (var i = 0; i < request.Transactions.Count; i++)
        {
            var row = request.Transactions[i];

            var transaction = ReversalTransaction.Create(
                batch.Id,
                batch.BatchReference,
                i + 1,
                row.TransactionType,
                row.SessionIdOrFtReference ?? string.Empty,
                row.Rrn,
                row.AccountNumber ?? string.Empty,
                row.TransactionDate,
                row.TransactionAmount,
                row.Channel ?? string.Empty,
                row.BeneficiaryBank,
                row.Biller,
                row.ReasonForFailure ?? string.Empty,
                row.Comments,
                _currentUser.Email ?? _currentUser.UserId);

            transaction.MarkValid();
            batch.AddTransaction(transaction);
        }

        batch.CompleteValidation();
        _batchRepository.Add(batch);

        _auditService.Record(
            AuditAction.Upload,
            nameof(ReversalBatch),
            batch.Id.ToString(),
            batch.BatchReference,
            $"Batch {batch.BatchReference} created with {batch.TotalRecords} records (all valid).");

        _auditService.Record(
            AuditAction.Validation,
            nameof(ReversalBatch),
            batch.Id.ToString(),
            batch.BatchReference,
            $"Validation complete for {batch.BatchReference}: {batch.ValidRecords} valid, 0 invalid.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Batch {BatchReference} created by {UserId}: {Total} records, all valid.",
            batch.BatchReference, _currentUser.Email ?? _currentUser.UserId, batch.TotalRecords);

        return MapToResultDto(batch);
    }

    public async Task<UploadBatchResultDto> GetResultAsync(string batchReference, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);
        return MapToResultDto(batch);
    }

    public async Task<PagedResult<TransactionRowDto>> GetRecordsAsync(string batchReference, int page, int pageSize, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);

        // A batch is capped at BusinessRulesOptions.MaxRecordsPerFile (BRU-01, default 500) and is
        // already loaded whole (GetBatchOrThrowAsync includes transactions), so paging is a cheap
        // in-memory slice rather than a second database round trip.
        var ordered = batch.Transactions.OrderBy(t => t.RowNumber).ToList();

        var pageItems = ordered
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .Select(MapToRowDto)
            .ToList();

        return PagedResult<TransactionRowDto>.Create(pageItems, page, pageSize, ordered.Count);
    }

    public async Task<UploadBatchResultDto> EditRecordAsync(
        string batchReference, Guid transactionId, EditTransactionRowRequest request, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);
        var transaction = batch.GetMutableTransaction(transactionId); // throws DomainException if batch/row isn't editable

        var (merged, errors) = await ValidateAsync(transaction, request, batch, transactionId, ct);

        transaction.EditFields(
            merged.TransactionType, merged.SessionIdOrFtReference, merged.Rrn, merged.AccountNumber,
            merged.TransactionDate, merged.TransactionAmount, merged.Channel, merged.BeneficiaryBank,
            merged.Biller, merged.ReasonForFailure, merged.Comments, _currentUser.UserId);

        if (errors.Count == 0)
            transaction.MarkValid();
        else
            transaction.MarkInvalid(errors);

        batch.RecalculateCounts();

        _auditService.Record(
            AuditAction.Validation,
            nameof(ReversalTransaction),
            transaction.Id.ToString(),
            batch.BatchReference,
            $"Row {transaction.RowNumber} edited by {_currentUser.UserId}: {(errors.Count == 0 ? "now valid" : $"still invalid - {string.Join("; ", errors)}")}.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Row {RowNumber} of batch {BatchReference} edited by {UserId}.", transaction.RowNumber, batch.BatchReference, _currentUser.UserId);

        return MapToResultDto(batch);
    }

    public async Task<UploadBatchResultDto> DeleteRecordAsync(string batchReference, Guid transactionId, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);
        var transaction = batch.GetMutableTransaction(transactionId);
        var rowNumber = transaction.RowNumber;
        var reference = transaction.SessionIdOrFtReference;

        batch.RemoveTransaction(transactionId);

        _auditService.Record(
            AuditAction.Validation,
            nameof(ReversalTransaction),
            transactionId.ToString(),
            batch.BatchReference,
            $"Row {rowNumber} ('{reference}') deleted from batch {batch.BatchReference} by {_currentUser.UserId}.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Row {RowNumber} of batch {BatchReference} deleted by {UserId}.", rowNumber, batch.BatchReference, _currentUser.UserId);

        return MapToResultDto(batch);
    }

    public async Task<UploadBatchResultDto> RetryValidationAsync(string batchReference, Guid transactionId, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);
        var transaction = batch.GetMutableTransaction(transactionId);

        var (_, errors) = await ValidateAsync(transaction, edit: null, batch, transactionId, ct);

        if (errors.Count == 0)
            transaction.MarkValid();
        else
            transaction.MarkInvalid(errors);

        batch.RecalculateCounts();

        _auditService.Record(
            AuditAction.Validation,
            nameof(ReversalTransaction),
            transaction.Id.ToString(),
            batch.BatchReference,
            $"Row {transaction.RowNumber} revalidated by {_currentUser.UserId}: {(errors.Count == 0 ? "now valid" : $"still invalid - {string.Join("; ", errors)}")}.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Row {RowNumber} of batch {BatchReference} retried by {UserId}.", transaction.RowNumber, batch.BatchReference, _currentUser.UserId);

        return MapToResultDto(batch);
    }

    public async Task<UploadBatchResultDto> SubmitForReviewAsync(string batchReference, CancellationToken ct = default)
    {
        var batch = await GetBatchOrThrowAsync(batchReference, ct);

        batch.SubmitForApproval(_currentUser.UserId, _currentUser.UserName);

        _auditService.Record(
            AuditAction.Submission,
            nameof(ReversalBatch),
            batch.Id.ToString(),
            batch.BatchReference,
            $"Batch {batch.BatchReference} submitted for approval with {batch.ValidRecords} valid record(s).");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Batch {BatchReference} submitted for review by {UserId}.", batch.BatchReference, _currentUser.UserId);

        // Approval routing, initiator -> authorizer: any active SettlementApprover or
        // Administrator may act on this batch (there is no single assigned authorizer per batch),
        // so every one of them is notified. Best-effort — a delivery failure here must never
        // undo the submission that already succeeded above.
        await NotifyApproversAsync(batch, ct);

        return MapToResultDto(batch);
    }

    private async Task NotifyApproversAsync(ReversalBatch batch, CancellationToken ct)
    {
        var approverEmails = await _contactDirectory.GetEmailsByRolesAsync(
            [AppRoles.SettlementApprover, AppRoles.Administrator], ct);

        var link = BuildPortalLink($"approvals/{batch.BatchReference}");
        var body =
            $"<p>Batch <b>{Encode(batch.BatchReference)}</b> ('{Encode(batch.BatchName)}') was submitted for approval " +
            $"by {Encode(batch.SubmittedByName ?? batch.UploadedByName)} with {batch.ValidRecords} valid record(s).</p>" +
            (link is null ? "" : $"<p><a href=\"{link}\">Open in the Settlement Portal</a></p>");

        await _emailService.SendAsync(approverEmails, $"Reversal batch {batch.BatchReference} awaiting your approval", body, ct);
    }

    private string? BuildPortalLink(string relativePath) =>
        string.IsNullOrWhiteSpace(_emailOptions.PortalBaseUrl) ? null : $"{_emailOptions.PortalBaseUrl.TrimEnd('/')}/{relativePath}";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private async Task<ReversalBatch> GetBatchOrThrowAsync(string batchReference, CancellationToken ct) =>
        await _batchRepository.GetByReferenceAsync(batchReference, includeTransactions: true, ct)
            ?? throw new NotFoundAppException(nameof(ReversalBatch), batchReference);

    /// <summary>Re-runs field-level validation plus the same cross-row (BRU-06), cross-system
    /// (BRU-04), and source-transaction (BRU-05) checks applied at batch creation, scoped to one row.</summary>
    private async Task<(TransactionRevalidationRequest Merged, List<string> Errors)> ValidateAsync(
        ReversalTransaction current, EditTransactionRowRequest? edit, ReversalBatch batch, Guid transactionId, CancellationToken ct)
    {
        var merged = BuildMergedRequest(current, edit);
        var errors = _fieldValidator.Validate(merged);

        if (!string.IsNullOrWhiteSpace(merged.SessionIdOrFtReference))
        {
            var otherRefs = batch.OtherReferences(transactionId);
            if (otherRefs.Contains(merged.SessionIdOrFtReference, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Session ID / FT Reference '{merged.SessionIdOrFtReference}' appears more than once in this batch.");

            if (await _transactionRepository.HasActiveConflictAsync(merged.SessionIdOrFtReference, transactionId, ct))
                errors.Add($"Session ID / FT Reference '{merged.SessionIdOrFtReference}' is already reversed or already active in another batch.");

            errors.AddRange(await CheckSourceTransactionAsync(merged.SessionIdOrFtReference, merged.AccountNumber, merged.TransactionAmount, ct));
        }

        return (merged, errors);
    }

    /// <summary>BRU-05 for a single reference. No-op (empty result) when the Transfer Service
    /// integration is disabled, or the reference is blank (field validation already flags that).</summary>
    private async Task<List<string>> CheckSourceTransactionAsync(string? reference, string? accountNumber, decimal amount, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!_transferOptions.Enabled || string.IsNullOrWhiteSpace(reference))
        {
            return errors;
        }

        var result = await _transferService.GetTransactionByReferenceAsync(reference, ct);

        switch (result.Status)
        {
            case TransferReferenceLookupStatus.NotFound:
                errors.Add($"Session ID / FT Reference '{reference}' does not match any source transaction record.");
                break;

            case TransferReferenceLookupStatus.Unavailable:
                errors.Add($"Unable to verify Session ID / FT Reference '{reference}' against source transaction records ({result.ErrorMessage}). Use Retry once the Transfer Service is reachable.");
                break;

            case TransferReferenceLookupStatus.Found:
                if (!string.IsNullOrWhiteSpace(result.AccountNumber) && !string.Equals(result.AccountNumber, accountNumber, StringComparison.Ordinal))
                    errors.Add($"Account Number '{accountNumber}' does not match the source transaction record for '{reference}'.");
                if (result.Amount.HasValue && result.Amount.Value != amount)
                    errors.Add($"Transaction Amount does not match the source transaction record for '{reference}'.");
                break;
        }

        return errors;
    }

    /// <summary>BRU-05 for a whole batch, bounded to <see cref="TransferServiceOptions.MaxConcurrentRequests"/>
    /// concurrent lookups. Returns one error list per row, aligned by index.</summary>
    private async Task<List<string>[]> CheckSourceTransactionsAsync(IReadOnlyList<TransactionRevalidationRequest> rows, CancellationToken ct)
    {
        var results = new List<string>[rows.Count];

        if (!_transferOptions.Enabled)
        {
            Array.Fill(results, []);
            return results;
        }

        using var semaphore = new SemaphoreSlim(Math.Max(1, _transferOptions.MaxConcurrentRequests));

        var tasks = rows.Select(async (row, index) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                results[index] = await CheckSourceTransactionAsync(row.SessionIdOrFtReference, row.AccountNumber, row.TransactionAmount, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private static TransactionRevalidationRequest BuildMergedRequest(ReversalTransaction current, EditTransactionRowRequest? edit) => new()
    {
        TransactionType = edit?.TransactionType ?? current.TransactionType,
        SessionIdOrFtReference = edit?.SessionIdOrFtReference ?? current.SessionIdOrFtReference,
        Rrn = edit?.Rrn ?? current.Rrn,
        AccountNumber = edit?.AccountNumber ?? current.AccountNumber,
        TransactionDate = edit?.TransactionDate ?? current.TransactionDate,
        TransactionAmount = edit?.TransactionAmount ?? current.TransactionAmount,
        Channel = edit?.Channel ?? current.Channel,
        BeneficiaryBank = edit?.BeneficiaryBank ?? current.BeneficiaryBank,
        Biller = edit?.Biller ?? current.Biller,
        ReasonForFailure = edit?.ReasonForFailure ?? current.ReasonForFailure,
        Comments = edit?.Comments ?? current.Comments
    };

    private static UploadBatchResultDto MapToResultDto(ReversalBatch batch)
    {
        var invalidRows = batch.Transactions
            .Where(t => t.RowValidationStatus == RowValidationStatus.Invalid)
            .OrderBy(t => t.RowNumber)
            .Select(t => new InvalidRowDto(
                t.RowNumber, t.SessionIdOrFtReference, (t.ValidationErrors ?? string.Empty).Split("; ", StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

        return new UploadBatchResultDto(
            batch.Id, batch.BatchReference, batch.BatchName, batch.Status,
            batch.TotalRecords, batch.ValidRecords, batch.InvalidRecords, invalidRows);
    }

    private static TransactionRowDto MapToRowDto(ReversalTransaction t) => new(
        t.Id, t.RowNumber, t.TransactionType, t.SessionIdOrFtReference, t.Rrn, t.AccountNumber,
        t.TransactionDate, t.TransactionAmount, t.Channel, t.BeneficiaryBank, t.Biller,
        t.ReasonForFailure, t.Comments, t.RowValidationStatus,
        (t.ValidationErrors ?? string.Empty).Split("; ", StringSplitOptions.RemoveEmptyEntries));
}
