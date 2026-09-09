using System.Text;
using Asp.Versioning;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Application.Features.Upload.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>Batch Upload screen (FR-03 to FR-09): validate and stage a batch of reversal
/// transactions; review, edit, delete, and revalidate individual rows; then submit for approval.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/upload")]
[Authorize(Roles = AppRoles.AnyUser)]
[Produces("application/json")]
public class UploadController : ControllerBase
{
    private const int MaxCsvUploadSizeBytes = 5_000_000;

    private readonly IBatchUploadService _uploadService;
    private readonly IReportExportService _exportService;
    private readonly IValidator<CreateReversalBatchRequest> _createValidator;
    private readonly IValidator<EditTransactionRowRequest> _editValidator;

    public UploadController(
        IBatchUploadService uploadService,
        IReportExportService exportService,
        IValidator<CreateReversalBatchRequest> createValidator,
        IValidator<EditTransactionRowRequest> editValidator)
    {
        _uploadService = uploadService;
        _exportService = exportService;
        _createValidator = createValidator;
        _editValidator = editValidator;
    }

    /// <summary>FR-05: downloadable copy of the current Reversal Upload Template. Pass
    /// <c>?format=csv</c> for a CSV copy of the same template; any other/omitted value returns the
    /// Excel (.xlsx) version.</summary>
    [HttpGet("template")]
    [AllowAnonymous] // Static, non-sensitive artifact; kept accessible without auth friction for convenience links.
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DownloadTemplate([FromQuery] string? format = "xlsx")
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = _exportService.BuildUploadTemplateCsv();
            return File(csv, "text/csv", "Reversal-Upload-Template.csv");
        }

        var bytes = _exportService.BuildUploadTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Reversal-Upload-Template.xlsx");
    }

    /// <summary>FR-03/FR-06/FR-07: validate a batch of transactions submitted as structured JSON.
    /// The batch stays Validated (editable) until explicitly submitted via <see cref="Submit"/>.</summary>
    [HttpPost]
    [RequestSizeLimit(5_000_000)]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadBatchResultDto>> Create([FromBody] CreateReversalBatchRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await _uploadService.CreateBatchAsync(request, ct);

        return CreatedAtAction(nameof(GetRecords), new { batchReference = result.BatchReference }, result);
    }

    /// <summary>FR-03/FR-06/FR-07, CSV variant: same as <see cref="Create"/>, but the transaction
    /// rows are supplied as an uploaded CSV file (matching the Reversal Upload Template's columns,
    /// FR-05) instead of a JSON array. Columns are matched by header name, not position.</summary>
    [HttpPost("csv")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxCsvUploadSizeBytes)]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadBatchResultDto>> CreateFromCsv(
        [FromForm] string batchName, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = ["A non-empty CSV file is required."] }));
        }

        if (file.Length > MaxCsvUploadSizeBytes)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = [$"The file exceeds the maximum size of {MaxCsvUploadSizeBytes / 1_000_000} MB."] }));
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = ["Only .csv files are accepted."] }));
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync(ct);
        }

        CreateReversalBatchRequest request;
        try
        {
            request = CsvBatchRequestParser.Parse(batchName, content);
        }
        catch (ValidationAppException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors)));
        }

        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await _uploadService.CreateBatchAsync(request, ct);

        return CreatedAtAction(nameof(GetRecords), new { batchReference = result.BatchReference }, result);
    }

    /// <summary>Staged rows for the Upload results/review screen.</summary>
    [HttpGet("{batchReference}/records")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TransactionRowDto>>> GetRecords(string batchReference, CancellationToken ct)
    {
        var records = await _uploadService.GetRecordsAsync(batchReference, ct);
        return Ok(records);
    }

    /// <summary>Corrects a single staged row and revalidates it in place.</summary>
    [HttpPut("{batchReference}/records/{transactionId:guid}")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> EditRecord(
        string batchReference, Guid transactionId, [FromBody] EditTransactionRowRequest request, CancellationToken ct)
    {
        var validation = await _editValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await _uploadService.EditRecordAsync(batchReference, transactionId, request, ct);
        return Ok(result);
    }

    /// <summary>Removes a single staged row from the batch.</summary>
    [HttpDelete("{batchReference}/records/{transactionId:guid}")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> DeleteRecord(string batchReference, Guid transactionId, CancellationToken ct)
    {
        var result = await _uploadService.DeleteRecordAsync(batchReference, transactionId, ct);
        return Ok(result);
    }

    /// <summary>Re-runs validation on a row's current values without changing them.</summary>
    [HttpPost("{batchReference}/records/{transactionId:guid}/retry")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> RetryRecord(string batchReference, Guid transactionId, CancellationToken ct)
    {
        var result = await _uploadService.RetryValidationAsync(batchReference, transactionId, ct);
        return Ok(result);
    }

    /// <summary>"Submit Records For Review": moves the batch into the Approvals queue (FR-09).</summary>
    [HttpPost("{batchReference}/submit")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> Submit(string batchReference, CancellationToken ct)
    {
        var result = await _uploadService.SubmitForReviewAsync(batchReference, ct);
        return Ok(result);
    }

    /// <summary>FR-08: error report for a batch's invalid rows, so Settlement can correct and
    /// resubmit only the failed records without resubmitting the whole batch.</summary>
    [HttpGet("{batchReference}/invalid-rows-report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvalidRowsReport(string batchReference, CancellationToken ct)
    {
        var result = await _uploadService.GetResultAsync(batchReference, ct);
        var bytes = _exportService.BuildInvalidRowsReport(result);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{result.BatchReference}-invalid-rows.xlsx");
    }
}
