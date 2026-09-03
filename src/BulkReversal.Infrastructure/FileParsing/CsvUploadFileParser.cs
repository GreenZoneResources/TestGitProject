using System.Globalization;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.Upload.Dtos;
using CsvHelper;
using CsvHelper.Configuration;

namespace BulkReversal.Infrastructure.FileParsing;

/// <summary>Parses the Reversal Upload Template in .csv form (BRU-02).</summary>
public class CsvUploadFileParser : IUploadFileParser
{
    public bool CanParse(string fileName) =>
        Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RawUploadRow>> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StreamReader(fileStream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            return [];
        }

        var headerMap = BuildHeaderMap(csv.HeaderRecord ?? []);
        var rows = new List<RawUploadRow>();
        var rowNumber = 0;

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;

            // A fully blank trailing row (common at file end) is skipped, not treated as data.
            if (IsBlankRow(csv, headerMap)) continue;

            rows.Add(new RawUploadRow
            {
                RowNumber = rowNumber,
                SerialNumber = Field(csv, headerMap, UploadTemplateColumns.SerialNumber),
                TransactionType = Field(csv, headerMap, UploadTemplateColumns.TransactionType),
                SessionIdOrFtReference = Field(csv, headerMap, UploadTemplateColumns.SessionIdOrFtReference),
                Rrn = Field(csv, headerMap, UploadTemplateColumns.Rrn),
                AccountNumber = Field(csv, headerMap, UploadTemplateColumns.AccountNumber),
                TransactionDate = Field(csv, headerMap, UploadTemplateColumns.TransactionDate),
                TransactionAmount = Field(csv, headerMap, UploadTemplateColumns.TransactionAmount),
                Channel = Field(csv, headerMap, UploadTemplateColumns.Channel),
                BeneficiaryBank = Field(csv, headerMap, UploadTemplateColumns.BeneficiaryBank),
                Biller = Field(csv, headerMap, UploadTemplateColumns.Biller),
                ReasonForFailure = Field(csv, headerMap, UploadTemplateColumns.ReasonForFailure),
                Comments = Field(csv, headerMap, UploadTemplateColumns.Comments)
            });
        }

        return rows;
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] headerRecord)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headerRecord.Length; i++)
        {
            map[UploadTemplateColumns.Normalize(headerRecord[i])] = i;
        }
        return map;
    }

    private static string? Field(CsvReader csv, Dictionary<string, int> headerMap, string canonicalHeader)
    {
        if (!headerMap.TryGetValue(UploadTemplateColumns.Normalize(canonicalHeader), out var index))
            return null;

        return csv.TryGetField<string>(index, out var value) ? value : null;
    }

    private static bool IsBlankRow(CsvReader csv, Dictionary<string, int> headerMap) =>
        headerMap.Values.All(i => string.IsNullOrWhiteSpace(csv.TryGetField<string>(i, out var v) ? v : null));
}
