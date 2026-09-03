using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.Upload.Dtos;
using ClosedXML.Excel;

namespace BulkReversal.Infrastructure.FileParsing;

/// <summary>Parses the Reversal Upload Template in .xlsx form (BRU-02). Reads the first worksheet only.</summary>
public class ExcelUploadFileParser : IUploadFileParser
{
    public bool CanParse(string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<RawUploadRow>> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The uploaded workbook contains no worksheets.");

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return Task.FromResult<IReadOnlyList<RawUploadRow>>([]);
        }

        var headerRow = usedRange.FirstRow();
        var headerMap = BuildHeaderMap(headerRow);

        var rows = new List<RawUploadRow>();
        var rowNumber = 0;

        foreach (var row in usedRange.RowsUsed().Skip(1))
        {
            ct.ThrowIfCancellationRequested();

            if (row.Cells().All(c => c.IsEmpty())) continue;

            rowNumber++;
            rows.Add(new RawUploadRow
            {
                RowNumber = rowNumber,
                SerialNumber = Field(row, headerMap, UploadTemplateColumns.SerialNumber),
                TransactionType = Field(row, headerMap, UploadTemplateColumns.TransactionType),
                SessionIdOrFtReference = Field(row, headerMap, UploadTemplateColumns.SessionIdOrFtReference),
                Rrn = Field(row, headerMap, UploadTemplateColumns.Rrn),
                AccountNumber = Field(row, headerMap, UploadTemplateColumns.AccountNumber),
                TransactionDate = Field(row, headerMap, UploadTemplateColumns.TransactionDate),
                TransactionAmount = Field(row, headerMap, UploadTemplateColumns.TransactionAmount),
                Channel = Field(row, headerMap, UploadTemplateColumns.Channel),
                BeneficiaryBank = Field(row, headerMap, UploadTemplateColumns.BeneficiaryBank),
                Biller = Field(row, headerMap, UploadTemplateColumns.Biller),
                ReasonForFailure = Field(row, headerMap, UploadTemplateColumns.ReasonForFailure),
                Comments = Field(row, headerMap, UploadTemplateColumns.Comments)
            });
        }

        return Task.FromResult<IReadOnlyList<RawUploadRow>>(rows);
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLRangeRow headerRow)
    {
        var map = new Dictionary<string, int>();
        foreach (var cell in headerRow.Cells())
        {
            var text = cell.GetString();
            if (string.IsNullOrWhiteSpace(text)) continue;
            map[UploadTemplateColumns.Normalize(text)] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static string? Field(IXLRangeRow row, Dictionary<string, int> headerMap, string canonicalHeader)
    {
        if (!headerMap.TryGetValue(UploadTemplateColumns.Normalize(canonicalHeader), out var columnNumber))
            return null;

        var cell = row.Cell(columnNumber - row.RangeAddress.FirstAddress.ColumnNumber + 1);

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("dd/MM/yyyy");

        var value = cell.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
