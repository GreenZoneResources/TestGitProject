namespace BulkReversal.Application.Common.Options;

/// <summary>
/// Configures how BulkReversal.API behaves as a "provider" to SingleReversalEngine.Orchestrator
/// (provider-integration-contract.md). Bound from appsettings.json ("ProviderIntegration").
/// </summary>
public class ProviderIntegrationOptions
{
    public const string SectionName = "ProviderIntegration";

    /// <summary>Upper bound on items returned per GET pending-reversals poll.</summary>
    public int MaxItemsPerPoll { get; set; } = 200;

    /// <summary>ISO currency code stamped on every pending item (the upload template carries NGN amounts only).</summary>
    public string Currency { get; set; } = "NGN";
}
