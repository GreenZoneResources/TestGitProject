using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Features.RoleManagement.Dtos;
using FluentValidation;

namespace BulkReversal.Application.Features.RoleManagement.Validators;

public class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
{
    public AssignRoleRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(role => AppRoles.Assignable.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AppRoles.Assignable)}.");
    }
}
