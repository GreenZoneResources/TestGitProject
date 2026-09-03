using BulkReversal.Application.Features.Approvals.Dtos;
using FluentValidation;

namespace BulkReversal.Application.Features.Approvals.Validators;

public class ApprovalRejectionRequestValidator : AbstractValidator<ApprovalDecisionRequest>
{
    public ApprovalRejectionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A rejection reason is required.")
            .MaximumLength(500);
    }
}
