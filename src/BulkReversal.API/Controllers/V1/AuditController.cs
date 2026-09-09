using Asp.Versioning;
using BulkReversal.API.Common;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Models;
using BulkReversal.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>FR-18: read access to the reversal lifecycle audit trail. Administrator-only —
/// audit data is compliance-sensitive and not part of the day-to-day Settlement workflow.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit")]
[Authorize(Roles = AppRoles.Administrator)]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditController(IAuditLogRepository auditLogRepository) => _auditLogRepository = auditLogRepository;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? batchReference,
        [FromQuery] string? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (this.ValidatePagination(page, pageSize) is { } invalid)
            return invalid;

        var (items, total) = await _auditLogRepository.SearchAsync(batchReference, entityId, page, pageSize, ct);
        return Ok(PagedResult<AuditLogEntry>.Create(items, page, pageSize, total));
    }
}
