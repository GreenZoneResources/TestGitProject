namespace BulkReversal.Application.Common.Options;

/// <summary>
/// Configurable business rules (BRD Section 5). Bound from appsettings.json ("BusinessRules")
/// so limits can be tuned per-environment without a code change (BRU-01).
/// </summary>
public class BusinessRulesOptions
{
    public const string SectionName = "BusinessRules";

    /// <summary>BRU-01: indicative limit of 500 records per uploaded file.</summary>
    public int MaxRecordsPerFile { get; set; } = 500;

    /// <summary>Accepted upload file extensions (BRU-02).</summary>
    public string[] AllowedFileExtensions { get; set; } = [".csv", ".xlsx"];

    /// <summary>Maximum accepted upload file size, in bytes.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>How many days in the past a transaction date may fall to still be accepted.</summary>
    public int MaxTransactionAgeDays { get; set; } = 365;
}
