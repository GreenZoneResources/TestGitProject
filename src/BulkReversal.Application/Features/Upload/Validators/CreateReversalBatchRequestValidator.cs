using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Upload.Dtos;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Upload.Validators;

/// <summary>
/// Guards only against structurally malformed/oversized requests (empty batch, no rows, too many
/// rows, oversized field values) — a fast, cheap rejection before the more expensive per-row
/// business validity checks (mandatory fields, account format, in-batch duplicates, cross-system
/// conflicts, source-transaction match) run in
/// <see cref="BulkReversal.Application.Features.Upload.Services.BatchUploadService.CreateBatchAsync"/>.
/// Both layers ultimately reject the whole batch (400) on any failure — nothing is ever persisted
/// unless every row is valid.
/// </summary>
public class CreateReversalBatchRequestValidator : AbstractValidator<CreateReversalBatchRequest>
{
    public CreateReversalBatchRequestValidator(IOptions<BusinessRulesOptions> rules)
    {
        var maxRecords = rules.Value.MaxRecordsPerFile;

        RuleFor(x => x.BatchName)
            .NotEmpty().WithMessage("Batch name is required.")
            .MaximumLength(200);

        RuleFor(x => x.Transactions)
            .NotEmpty().WithMessage("At least one transaction is required.")
            .Must(t => t.Count <= maxRecords).WithMessage($"A batch may contain at most {maxRecords} records.");

        RuleForEach(x => x.Transactions).SetValidator(new TransactionRevalidationRequestValidator());
    }
}

public class TransactionRevalidationRequestValidator : AbstractValidator<TransactionRevalidationRequest>
{
    public TransactionRevalidationRequestValidator()
    {
        RuleFor(x => x.SessionIdOrFtReference).MaximumLength(100);
        RuleFor(x => x.Rrn).MaximumLength(50);
        RuleFor(x => x.AccountNumber).MaximumLength(20);
        RuleFor(x => x.Channel).MaximumLength(50);
        RuleFor(x => x.BeneficiaryBank).MaximumLength(100);
        RuleFor(x => x.Biller).MaximumLength(100);
        RuleFor(x => x.ReasonForFailure).MaximumLength(500);
        RuleFor(x => x.Comments).MaximumLength(1000);
    }
}
