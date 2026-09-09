using Asp.Versioning;
using BulkReversal.API.Common;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Features.Approvals;
using BulkReversal.Application.Features.Approvals.Dtos;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>Approvals screen: review and action reversal batches awaiting sign-off (FR-09/FR-10).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/approvals")]
[Authorize(Roles = AppRoles.AnyUser)]
[Produces("application/json")]
public class ApprovalsController : ControllerBase
{
    private readonly IApprovalService _approvalService;
    private readonly IValidator<ApprovalDecisionRequest> _rejectionValidator;

    public ApprovalsController(IApprovalService approvalService, IValidator<ApprovalDecisionRequest> rejectionValidator)
    {
        _approvalService = approvalService;
        _rejectionValidator = rejectionValidator;
    }

    /// <summary>Batches awaiting sign-off, optionally filtered by batch reference.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? batchReference, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (this.ValidatePagination(page, pageSize) is { } invalid)
            return invalid;

        var result = await _approvalService.SearchPendingAsync(batchReference, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Batch detail with its valid transaction rows, for review before sign-off.</summary>
    [HttpGet("{batchReference}")]
    [ProducesResponseType(typeof(ApprovalDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovalDetailDto>> GetDetail(
        string batchReference, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (this.ValidatePagination(page, pageSize) is { } invalid)
            return invalid;

        var result = await _approvalService.GetDetailAsync(batchReference, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Authorizes the batch: its valid rows become eligible for pickup by the reversal engine (FR-10).</summary>
    [HttpPost("{batchReference}/approve")]
    [Authorize(Roles = AppRoles.ApproverOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(string batchReference, CancellationToken ct)
    {
        await _approvalService.ApproveAsync(batchReference, ct);
        return NoContent();
    }

    /// <summary>Rejects the batch; no rows are released to the reversal engine.</summary>
    [HttpPost("{batchReference}/reject")]
    [Authorize(Roles = AppRoles.ApproverOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(string batchReference, [FromBody] ApprovalDecisionRequest request, CancellationToken ct)
    {
        var validation = await _rejectionValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        await _approvalService.RejectAsync(batchReference, request.Reason!, ct);
        return NoContent();
    }
}
