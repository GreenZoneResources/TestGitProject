using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Upload.Services;

/// <summary>
/// Orchestrates FR-03 through FR-09: parses the uploaded template, runs field-level and
/// cross-row/cross-system validation (Section 5 business rules), stages the batch, and assigns
/// its Batch Reference Number.
/// </summary>
public class BatchUploadService : IBatchUploadService
{
    private readonly IEnumerable<IUploadFileParser> _parsers;
    private readonly UploadRowFieldValidator _fieldValidator;
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IReversalTransactionRepository _transactionRepository;
    private readonly IBatchReferenceGenerator _referenceGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly BusinessRulesOptions _rules;
    private readonly ILogger<BatchUploadService> _logger;

    public BatchUploadService(
        IEnumerable<IUploadFileParser> parsers,
        UploadRowFieldValidator fieldValidator,
        IReversalBatchRepository batchRepository,
        IReversalTransactionRepository transactionRepository,
        IBatchReferenceGenerator referenceGenerator,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IOptions<BusinessRulesOptions> rules,
        ILogger<BatchUploadService> logger)
    {
        _parsers = parsers;
        _fieldValidator = fieldValidator;
        _batchRepository = batchRepository;
        _transactionRepository = transactionRepository;
        _referenceGenerator = referenceGenerator;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _auditService = auditService;
        _rules = rules.Value;
        _logger = logger;
    }

    public async Task<UploadBatchResultDto> UploadAsync(UploadBatchCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.BatchName))
            throw new ValidationAppException(nameof(command.BatchName), "Batch name is required.");

        if (command.FileSizeBytes <= 0)
            throw new ValidationAppException(nameof(command.FileName), "The uploaded file is empty.");

        if (command.FileSizeBytes > _rules.MaxFileSizeBytes)
            throw new ValidationAppException(nameof(command.FileName), $"File exceeds the maximum allowed size of {_rules.MaxFileSizeBytes / (1024 * 1024)} MB.");

        var extension = Path.GetExtension(command.FileName);
        if (!_rules.AllowedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ValidationAppException(nameof(command.FileName), $"Unsupported file type '{extension}'. Only {string.Join(", ", _rules.AllowedFileExtensions)} are accepted."); // BRU-02

        var parser = _parsers.FirstOrDefault(p => p.CanParse(command.FileName))
            ?? throw new ValidationAppException(nameof(command.FileName), $"No parser is registered for file type '{extension}'.");

        var rawRows = await parser.ParseAsync(command.FileStream, command.FileName, ct);

        if (rawRows.Count == 0)
            throw new ValidationAppException(nameof(command.FileName), "The uploaded file contains no data rows.");

        if (rawRows.Count > _rules.MaxRecordsPerFile) // BRU-01
            throw new ValidationAppException(nameof(command.FileName), $"File contains {rawRows.Count} records, exceeding the maximum of {_rules.MaxRecordsPerFile} records per file.");

        var batchReference = await _referenceGenerator.NextAsync(ct);
        var batch = ReversalBatch.Create(command.BatchName, command.FileName, _currentUser.UserId, _currentUser.UserName, batchReference);

        var parsedRows = rawRows.Select(r => (Raw: r, Parsed: _fieldValidator.Validate(r))).ToList();

        // BRU-06: duplicate references within the same file.
        var duplicateRefsInFile = parsedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Parsed.SessionIdOrFtReference))
            .GroupBy(x => x.Parsed.SessionIdOrFtReference, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // BRU-04: references already reversed anywhere in the system.
        var candidateRefs = parsedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Parsed.SessionIdOrFtReference))
            .Select(x => x.Parsed.SessionIdOrFtReference)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var alreadyReversedRefs = await _transactionRepository.GetAlreadyReversedReferencesAsync(candidateRefs, ct);

        var invalidRows = new List<InvalidRowDto>();

        foreach (var (raw, parsed) in parsedRows)
        {
            var errors = new List<string>(parsed.Errors);

            if (!string.IsNullOrWhiteSpace(parsed.SessionIdOrFtReference))
            {
                if (duplicateRefsInFile.Contains(parsed.SessionIdOrFtReference))
                    errors.Add($"Session ID / FT Reference '{parsed.SessionIdOrFtReference}' appears more than once in this file.");

                if (alreadyReversedRefs.Contains(parsed.SessionIdOrFtReference))
                    errors.Add($"Session ID / FT Reference '{parsed.SessionIdOrFtReference}' has already been reversed.");
            }

            var transaction = ReversalTransaction.Create(
                batch.Id,
                batch.BatchReference,
                raw.RowNumber,
                parsed.TransactionType,
                parsed.SessionIdOrFtReference,
                parsed.Rrn,
                parsed.AccountNumber,
                parsed.TransactionDate,
                parsed.TransactionAmount,
                parsed.Channel,
                parsed.BeneficiaryBank,
                parsed.Biller,
                parsed.ReasonForFailure,
                parsed.Comments,
                _currentUser.UserId);

            if (errors.Count == 0)
            {
                transaction.MarkValid();
            }
            else
            {
                transaction.MarkInvalid(errors);
                invalidRows.Add(new InvalidRowDto(raw.RowNumber, parsed.SessionIdOrFtReference, errors));
            }

            batch.AddTransaction(transaction);
        }

        batch.CompleteValidation();

        // The upload screen offers a single "Upload & Validate" action (no separate submit step);
        // a batch with at least one valid row flows straight into the Approvals queue (FR-09).
        if (batch.ValidRecords > 0)
        {
            batch.SubmitForApproval(_currentUser.UserId, _currentUser.UserName);
        }

        _batchRepository.Add(batch);

        _auditService.Record(
            AuditAction.Upload,
            nameof(ReversalBatch),
            batch.Id.ToString(),
            batch.BatchReference,
            $"Uploaded '{command.FileName}' as batch {batch.BatchReference}: {batch.TotalRecords} records ({batch.ValidRecords} valid, {batch.InvalidRecords} invalid).");

        _auditService.Record(
            AuditAction.Validation,
            nameof(ReversalBatch),
            batch.Id.ToString(),
            batch.BatchReference,
            $"Validation complete for {batch.BatchReference}: {batch.ValidRecords} valid, {batch.InvalidRecords} invalid.");

        if (batch.Status == BatchStatus.PendingApproval)
        {
            _auditService.Record(
                AuditAction.Submission,
                nameof(ReversalBatch),
                batch.Id.ToString(),
                batch.BatchReference,
                $"Batch {batch.BatchReference} submitted for approval with {batch.ValidRecords} valid record(s).");
        }

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Batch {BatchReference} uploaded by {UserId}: {Total} records, {Valid} valid, {Invalid} invalid.",
            batch.BatchReference, _currentUser.UserId, batch.TotalRecords, batch.ValidRecords, batch.InvalidRecords);

        return new UploadBatchResultDto(
            batch.Id,
            batch.BatchReference,
            batch.BatchName,
            batch.TotalRecords,
            batch.ValidRecords,
            batch.InvalidRecords,
            invalidRows);
    }

    public async Task<UploadBatchResultDto> GetResultAsync(string batchReference, CancellationToken ct = default)
    {
        var batch = await _batchRepository.GetByReferenceAsync(batchReference, includeTransactions: true, ct)
            ?? throw new Common.Exceptions.NotFoundAppException(nameof(Domain.Entities.ReversalBatch), batchReference);

        var invalidRows = batch.Transactions
            .Where(t => t.RowValidationStatus == RowValidationStatus.Invalid)
            .OrderBy(t => t.RowNumber)
            .Select(t => new InvalidRowDto(
                t.RowNumber, t.SessionIdOrFtReference, (t.ValidationErrors ?? string.Empty).Split("; ", StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

        return new UploadBatchResultDto(
            batch.Id, batch.BatchReference, batch.BatchName, batch.TotalRecords, batch.ValidRecords, batch.InvalidRecords, invalidRows);
    }
}
