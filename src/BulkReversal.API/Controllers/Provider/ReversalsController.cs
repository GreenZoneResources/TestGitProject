using BulkReversal.Application.Features.Provider;
using BulkReversal.Application.Features.Provider.Dtos;
using BulkReversal.Infrastructure.Auth.ProviderApiKey;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BulkReversal.API.Controllers.Provider;

/// <summary>
/// The two endpoints SingleReversalEngine.Orchestrator calls, per provider-integration-contract.md.
/// BulkReversal.API is the "provider" in that contract. Routes are intentionally unversioned and
/// stable — they're configured once as PendingReversalsPath/CallbackUrl on the engine's provider
/// row and must not move when the Settlement-portal API versions forward.
/// </summary>
[ApiController]
[Route("api/reversals")]
[Authorize(AuthenticationSchemes = ProviderApiKeyOptions.SchemeName, Policy = ProviderApiKeyOptions.SchemeName)]
[EnableRateLimiting(RateLimiterPolicies.Provider)]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "provider")]
public class ReversalsController : ControllerBase
{
    private readonly IProviderIntegrationService _providerService;
    private readonly IValidator<ReversalCallbackRequest> _callbackValidator;

    public ReversalsController(
        IProviderIntegrationService providerService,
        IValidator<ReversalCallbackRequest> callbackValidator)
    {
        _providerService = providerService;
        _callbackValidator = callbackValidator;
    }

    /// <summary>
    /// §1: "Which reversals are waiting for me to process?" Returns a bare JSON array (one of the
    /// two accepted response shapes) of items not yet acknowledged via <see cref="Callback"/>.
    /// </summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingReversalItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PendingReversalItemDto>>> GetPending(
        [FromQuery] int maxItems = 0, CancellationToken ct = default)
    {
        var items = await _providerService.GetPendingReversalsAsync(maxItems, ct);
        return Ok(items);
    }

    /// <summary>
    /// §2: "Here is the outcome of the reversal you told me about." Idempotent by msgId (§2.5) —
    /// a repeat delivery for an already-terminal msgId still returns 2xx, as the contract requires.
    /// </summary>
    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback([FromBody] ReversalCallbackRequest request, CancellationToken ct)
    {
        var validation = await _callbackValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var outcome = await _providerService.ApplyCallbackAsync(request, ct);

        return outcome switch
        {
            CallbackOutcome.Applied => Ok(),
            CallbackOutcome.MalformedMsgId => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["msgId"] = ["msgId is not a recognized format."] })),
            // §2.5 requires the callback handler to be idempotent for a repeat/unknown-by-now msgId;
            // we still surface 404 here since an msgId we never issued is a genuine integration
            // problem worth the engine's retry-and-alert path, not a silent 200.
            CallbackOutcome.UnknownMsgId => NotFound(new ProblemDetails
            {
                Title = "Unknown msgId.",
                Detail = $"No reversal item was found for msgId '{request.MsgId}'.",
                Status = StatusCodes.Status404NotFound
            }),
            _ => Problem()
        };
    }
}
