using System.Text.Json.Serialization;

namespace BulkReversal.Application.Features.Provider.Dtos;

/// <summary>
/// Wire-exact shape expected by SingleReversalEngine.Orchestrator's polling client
/// (provider-integration-contract.md §1.3). Property names are pinned with
/// <see cref="JsonPropertyNameAttribute"/> so they stay correct regardless of the host's global
/// JSON naming policy.
/// </summary>
public class PendingReversalItemDto
{
    /// <summary>Our own correlation id for this item; echoed back verbatim in the callback (§2.3).</summary>
    [JsonPropertyName("msgId")]
    public string MsgId { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "NGN";

    [JsonPropertyName("debtorAccountNumber")]
    public string DebtorAccountNumber { get; init; } = string.Empty;

    /// <summary>The idempotency key — the original funds-transfer reference being reversed (§1.3).</summary>
    [JsonPropertyName("ftReference")]
    public string FtReference { get; init; } = string.Empty;
}

/// <summary>
/// Wire-exact shape of the callback POSTed by the reversal engine (provider-integration-contract.md
/// §2.3). Correlates by <see cref="MsgId"/> only — ftReference is not present here.
/// </summary>
public class ReversalCallbackRequest
{
    [JsonPropertyName("msgId")]
    public string MsgId { get; init; } = string.Empty;

    [JsonPropertyName("isSuccessful")]
    public bool IsSuccessful { get; init; }

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; init; }

    [JsonPropertyName("failureCode")]
    public string? FailureCode { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}
