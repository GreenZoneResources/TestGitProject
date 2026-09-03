namespace BulkReversal.API;

public static class RateLimiterPolicies
{
    /// <summary>Applied to the reversal-engine-facing endpoints to blunt credential-stuffing/abuse
    /// against the API key, without throttling legitimate polling cadences (engine polls per its
    /// configured CronExpression, typically every few minutes).</summary>
    public const string Provider = "provider";
}
