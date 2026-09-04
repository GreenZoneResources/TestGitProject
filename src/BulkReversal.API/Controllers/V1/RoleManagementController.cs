using Asp.Versioning;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Features.RoleManagement;
using BulkReversal.Application.Features.RoleManagement.Dtos;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>
/// Administrator-only management of locally-assigned BulkReversal roles (SettlementUser /
/// SettlementApprover / Administrator). These assignments are merged into a caller's roles by the
/// SSO authentication middleware alongside the SSO "appRoles" claim, and back the approval-routing
/// email notifications (who counts as an authorizer to notify when a batch is submitted).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/role-assignments")]
[Authorize(Roles = AppRoles.Administrator)]
[Produces("application/json")]
public class RoleManagementController : ControllerBase
{
    private readonly IRoleAssignmentService _roleAssignmentService;
    private readonly IValidator<AssignRoleRequest> _assignValidator;

    public RoleManagementController(IRoleAssignmentService roleAssignmentService, IValidator<AssignRoleRequest> assignValidator)
    {
        _roleAssignmentService = roleAssignmentService;
        _assignValidator = assignValidator;
    }

    /// <summary>All role assignments, active and revoked, most recent first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleAssignmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleAssignmentDto>>> List(CancellationToken ct)
    {
        var result = await _roleAssignmentService.ListAsync(ct);
        return Ok(result);
    }

    /// <summary>Grants a role to a user. Takes effect on that user's next sign-in (roles are
    /// resolved once, at JWT validation time).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleAssignmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleAssignmentDto>> Assign([FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        var validation = await _assignValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await _roleAssignmentService.AssignRoleAsync(request, ct);
        return CreatedAtAction(nameof(List), new { }, result);
    }

    /// <summary>Revokes a role assignment. Takes effect on that user's next sign-in.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _roleAssignmentService.RevokeRoleAsync(id, ct);
        return NoContent();
    }
}
