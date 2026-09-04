using System.Net;
using System.Text.Json;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Infrastructure.ExternalServices;

/// <summary>
/// HTTP client for the core-banking Transfer Service's reference lookup (BRU-05). Builds the
/// request URL by concatenating BaseUrl + path, matching the Transfer Service's own convention;
/// the reference number is the only caller-controlled input in that URL and is percent-encoded via
/// <see cref="Uri.EscapeDataString"/> before it goes anywhere near string concatenation, so it can
/// never be used to redirect the request to a different host/path or inject extra query parameters.
/// </summary>
public class TransferServiceClient : ITransferService
{
    private const int MaxAttempts = 2;

    private readonly HttpClient _httpClient;
    private readonly TransferServiceOptions _options;
    private readonly ILogger<TransferServiceClient> _logger;

    public TransferServiceClient(HttpClient httpClient, IOptions<TransferServiceOptions> options, ILogger<TransferServiceClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransferReferenceLookupResult> GetTransactionByReferenceAsync(string referenceNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenceNumber))
        {
            return TransferReferenceLookupResult.Unavailable("A reference number is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.GetTransactionByReference))
        {
            _logger.LogError("TransferService:Enabled is true but BaseUrl/GetTransactionByReference is not configured.");
            return TransferReferenceLookupResult.Unavailable("Transfer Service is not configured.");
        }

        var url = _options.BaseUrl + $"{_options.GetTransactionByReference}/{Uri.EscapeDataString(referenceNumber.Trim())}";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(_options.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("ApiKey", _options.ApiKey);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return TransferReferenceLookupResult.NotFound();
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Transfer Service returned HTTP {StatusCode} for a reference lookup.", (int)response.StatusCode);
                    return TransferReferenceLookupResult.Unavailable($"Transfer Service returned HTTP {(int)response.StatusCode}.");
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                return ParseSuccessBody(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(ex, "Transfer Service lookup attempt {Attempt} failed; retrying.", attempt);
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
                    continue;
                }

                _logger.LogWarning(ex, "Transfer Service lookup failed after {Attempts} attempt(s).", MaxAttempts);
                return TransferReferenceLookupResult.Unavailable("Transfer Service is currently unreachable.");
            }
        }

        // Unreachable: the loop above always returns or throws by the final attempt.
        return TransferReferenceLookupResult.Unavailable("Transfer Service is currently unreachable.");
    }

    private static TransferReferenceLookupResult ParseSuccessBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return TransferReferenceLookupResult.Found(null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return TransferReferenceLookupResult.Found(null, null);
            }

            if (TryGetProperty(root, "found", out var foundProp) && foundProp.ValueKind == JsonValueKind.False)
            {
                return TransferReferenceLookupResult.NotFound();
            }

            var accountNumber = TryGetString(root, "accountNumber") ?? TryGetString(root, "debtorAccountNumber");
            var amount = TryGetDecimal(root, "amount");

            return TransferReferenceLookupResult.Found(accountNumber, amount);
        }
        catch (JsonException)
        {
            // A 2xx with an unparsable body still means the reference was found (that's what the
            // status code told us); we just have no fields left to cross-check.
            return TransferReferenceLookupResult.Found(null, null);
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? TryGetDecimal(JsonElement root, string name) =>
        TryGetProperty(root, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
}
