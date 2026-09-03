using BulkReversal.Application.Features.Provider.Dtos;
using FluentValidation;

namespace BulkReversal.Application.Features.Provider.Validators;

/// <summary>Guards the Wisdom-facing callback endpoint (§2.3) against malformed payloads before
/// they reach the correlation/idempotency logic.</summary>
public class ReversalCallbackRequestValidator : AbstractValidator<ReversalCallbackRequest>
{
    public ReversalCallbackRequestValidator()
    {
        RuleFor(x => x.MsgId)
            .NotEmpty().WithMessage("msgId is required.")
            .MaximumLength(64);

        RuleFor(x => x.ExternalReference).MaximumLength(128);
        RuleFor(x => x.FailureCode).MaximumLength(64);
        RuleFor(x => x.FailureReason).MaximumLength(1024);
    }
}
