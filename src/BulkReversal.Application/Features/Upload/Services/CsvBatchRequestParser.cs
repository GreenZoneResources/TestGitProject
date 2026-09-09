using System.Globalization;
using System.Text;
using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Upload.Services;

/// <summary>
/// Parses a CSV upload (matching the Reversal Upload Template's columns, FR-05) into the same
/// <see cref="CreateReversalBatchRequest"/> the JSON upload endpoint accepts, so both paths share
/// one business-validation pipeline downstream (<see cref="BatchUploadService.CreateBatchAsync"/>).
/// Columns are matched by header name (case-insensitive), not position, so a caller's column order
/// doesn't have to match the template exactly. Malformed input never throws an unhandled exception —
/// every failure surfaces as a <see cref="ValidationAppException"/> (400) or, for a per-row value
/// that can't be parsed, as a normal per-row validation error from the existing field validator
/// (e.g. an unparsable amount becomes "Transaction Amount must be greater than zero.").
/// </summary>
public static class CsvBatchRequestParser
{
    private static readonly string[] RequiredColumns =
    [
        UploadTemplateColumns.TransactionType,
        UploadTemplateColumns.SessionIdOrFtReference,
        UploadTemplateColumns.AccountNumber,
        UploadTemplateColumns.TransactionDate,
        UploadTemplateColumns.TransactionAmount,
        UploadTemplateColumns.Channel,
        UploadTemplateColumns.ReasonForFailure
    ];

    public static CreateReversalBatchRequest Parse(string batchName, string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            throw new ValidationAppException("file", "The uploaded CSV file is empty.");

        var rows = ParseRows(csvContent);
        if (rows.Count == 0)
            throw new ValidationAppException("file", "The uploaded CSV file has no header row.");

        var columnIndex = BuildColumnIndex(rows[0]);

        var missing = RequiredColumns.Where(c => !columnIndex.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new ValidationAppException("file", $"The CSV is missing required column(s): {string.Join(", ", missing)}.");

        var request = new CreateReversalBatchRequest { BatchName = batchName };

        for (var r = 1; r < rows.Count; r++)
        {
            var cols = rows[r];
            if (cols.Count == 0 || (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])))
                continue; // blank trailing line

            request.Transactions.Add(new TransactionRevalidationRequest
            {
                TransactionType = ParseTransactionType(Field(cols, columnIndex, UploadTemplateColumns.TransactionType)),
                SessionIdOrFtReference = Field(cols, columnIndex, UploadTemplateColumns.SessionIdOrFtReference),
                Rrn = OptionalField(cols, columnIndex, UploadTemplateColumns.Rrn),
                AccountNumber = Field(cols, columnIndex, UploadTemplateColumns.AccountNumber),
                TransactionDate = ParseDate(Field(cols, columnIndex, UploadTemplateColumns.TransactionDate)),
                TransactionAmount = ParseAmount(Field(cols, columnIndex, UploadTemplateColumns.TransactionAmount)),
                Channel = Field(cols, columnIndex, UploadTemplateColumns.Channel),
                BeneficiaryBank = OptionalField(cols, columnIndex, UploadTemplateColumns.BeneficiaryBank),
                Biller = OptionalField(cols, columnIndex, UploadTemplateColumns.Biller),
                ReasonForFailure = Field(cols, columnIndex, UploadTemplateColumns.ReasonForFailure),
                Comments = OptionalField(cols, columnIndex, UploadTemplateColumns.Comments)
            });
        }

        return request;
    }

    // Deliberately defaults to an obviously-invalid enum value (0) rather than throwing — the
    // existing UploadRowFieldValidator already rejects an undefined TransactionType with a clear
    // per-row message, so a bad CSV cell fails that one row instead of the whole batch.
    private static TransactionType ParseTransactionType(string value)
    {
        var normalized = value.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (Enum.TryParse<TransactionType>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        return default;
    }

    private static DateOnly ParseDate(string value)
    {
        string[] formats = ["dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "M/d/yyyy", "dd-MM-yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
            return loose;
        return default;
    }

    private static decimal ParseAmount(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    private static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> header)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (name.Length > 0 && !index.ContainsKey(name))
                index[name] = i;
        }
        return index;
    }

    private static string Field(IReadOnlyList<string> cols, IReadOnlyDictionary<string, int> columnIndex, string column) =>
        OptionalField(cols, columnIndex, column) ?? string.Empty;

    private static string? OptionalField(IReadOnlyList<string> cols, IReadOnlyDictionary<string, int> columnIndex, string column)
    {
        if (!columnIndex.TryGetValue(column, out var i) || i >= cols.Count)
            return null;
        var value = cols[i].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Minimal RFC 4180 tokenizer: handles quoted fields, embedded commas/newlines/quotes
    /// (doubled "" as an escaped quote), and both \n and \r\n line endings.</summary>
    private static List<List<string>> ParseRows(string content)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        var rowHasContent = false;

        void EndField()
        {
            currentRow.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            rows.Add(currentRow);
            currentRow = [];
            rowHasContent = false;
        }

        while (i < content.Length)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    i++;
                    break;
                case ',':
                    rowHasContent = true;
                    EndField();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    EndRow();
                    i++;
                    break;
                default:
                    rowHasContent = true;
                    field.Append(c);
                    i++;
                    break;
            }
        }

        if (rowHasContent || field.Length > 0 || currentRow.Count > 0)
        {
            EndRow();
        }

        return rows;
    }
}
