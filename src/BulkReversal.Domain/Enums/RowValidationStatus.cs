namespace BulkReversal.Domain.Enums;

/// <summary>Upload-time (front-end/API-level) validation outcome for a single row, per FR-06/FR-07.</summary>
public enum RowValidationStatus
{
    Valid = 1,
    Invalid = 2
}
