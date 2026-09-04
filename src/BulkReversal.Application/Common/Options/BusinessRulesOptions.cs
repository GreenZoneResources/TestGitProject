namespace BulkReversal.Application.Common.Options;

/// <summary>
/// Configurable business rules (BRD Section 5). Bound from appsettings.json ("BusinessRules")
/// so limits can be tuned per-environment without a code change (BRU-01).
/// </summary>
public class BusinessRulesOptions
{
    public const string SectionName = "BusinessRules";

    /// <summary>BRU-01: indicative limit of 500 records per submitted batch.</summary>
    public int MaxRecordsPerFile { get; set; } = 500;

    /// <summary>How many days in the past a transaction date may fall to still be accepted.</summary>
    public int MaxTransactionAgeDays { get; set; } = 365;
}
