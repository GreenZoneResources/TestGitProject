namespace BulkReversal.Application.Common.Options;

/// <summary>
/// Configures the BRU-05 source-transaction check against the core-banking Transfer Service.
/// Bound from appsettings.json ("TransferService"). Off by default (<see cref="Enabled"/> = false)
/// so environments without this integration ready keep working; flip it on once BaseUrl/ApiKey are
/// configured for the target environment.
/// </summary>
public class TransferServiceOptions
{
    public const string SectionName = "TransferService";

    public bool Enabled { get; set; }

    /// <summary>Scheme + host (+ port) only, no trailing slash — the lookup path is appended as a
    /// raw string, matching the Transfer Service's own URL convention.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path segment prefixed to the (percent-encoded) reference number, e.g.
    /// "/api/transactions/reference".</summary>
    public string GetTransactionByReference { get; set; } = string.Empty;

    /// <summary>Sent as the "ApiKey" request header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Upper bound on concurrent lookups when validating a batch, so a large upload
    /// doesn't open hundreds of simultaneous connections to the Transfer Service.</summary>
    public int MaxConcurrentRequests { get; set; } = 10;
}
