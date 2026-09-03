using BulkReversal.Application.Features.Upload.Dtos;
using FluentValidation;

namespace BulkReversal.Application.Features.Upload.Validators;

/// <summary>
/// Guards only against malformed/oversized payloads. Business validity (mandatory fields, account
/// number format, duplicates, ...) is intentionally NOT enforced here — an edit that leaves a row
/// still invalid is a normal outcome the row's own re-validation surfaces (FR-06/FR-07), not an
/// HTTP-level rejection.
/// </summary>
public class EditTransactionRowRequestValidator : AbstractValidator<EditTransactionRowRequest>
{
    public EditTransactionRowRequestValidator()
    {
        RuleFor(x => x.SessionIdOrFtReference).MaximumLength(100);
        RuleFor(x => x.Rrn).MaximumLength(50);
        RuleFor(x => x.AccountNumber).MaximumLength(20);
        RuleFor(x => x.Channel).MaximumLength(50);
        RuleFor(x => x.BeneficiaryBank).MaximumLength(100);
        RuleFor(x => x.Biller).MaximumLength(100);
        RuleFor(x => x.ReasonForFailure).MaximumLength(500);
        RuleFor(x => x.Comments).MaximumLength(1000);
        RuleFor(x => x.TransactionAmount).GreaterThan(0).When(x => x.TransactionAmount.HasValue);
    }
}
