using System.Net;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Options;
using BulkReversal.Infrastructure.ExternalServices;
using BulkReversal.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BulkReversal.UnitTests.ExternalServices;

public class TransferServiceClientTests
{
    private static readonly TransferServiceOptions BaseOptions = new()
    {
        Enabled = true,
        BaseUrl = "https://transfer.example.com",
        GetTransactionByReference = "/api/transactions/reference",
        ApiKey = "secret-key",
        TimeoutSeconds = 5
    };

    private static TransferServiceClient CreateSut(FakeHttpMessageHandler handler, TransferServiceOptions? options = null)
    {
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new TransferServiceClient(httpClient, Options.Create(options ?? BaseOptions), NullLogger<TransferServiceClient>.Instance);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_BuildsUrlByConcatenatingBaseUrlAndPath()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var sut = CreateSut(handler);

        await sut.GetTransactionByReferenceAsync("FT-001/A B");

        var requestedUrl = handler.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.Equal("https://transfer.example.com/api/transactions/reference/FT-001%2FA%20B", requestedUrl);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_SendsApiKeyHeader()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var sut = CreateSut(handler);

        await sut.GetTransactionByReferenceAsync("FT-001");

        var request = handler.Requests.Single();
        Assert.True(request.Headers.TryGetValues("ApiKey", out var values));
        Assert.Equal("secret-key", values!.Single());
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_HttpNotFound_ReturnsNotFound()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_SuccessWithJsonBody_ReturnsFoundWithParsedFields()
    {
        const string body = """{ "found": true, "accountNumber": "0123456789", "amount": 50000.00 }""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = CreateSut(handler);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.Found, result.Status);
        Assert.Equal("0123456789", result.AccountNumber);
        Assert.Equal(50000.00m, result.Amount);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_SuccessWithFoundFalse_ReturnsNotFound()
    {
        const string body = """{ "found": false }""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var sut = CreateSut(handler);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_ServerError_ReturnsUnavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_ConnectionFailure_RetriesOnceThenReturnsUnavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = CreateSut(handler);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.Unavailable, result.Status);
        Assert.Equal(2, handler.CallCount); // one retry, per TransferServiceClient.MaxAttempts
    }

    [Fact]
    public async Task GetTransactionByReferenceAsync_NotConfigured_ReturnsUnavailableWithoutCallingHttp()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var options = new TransferServiceOptions { Enabled = true, BaseUrl = "", GetTransactionByReference = "" };
        var sut = CreateSut(handler, options);

        var result = await sut.GetTransactionByReferenceAsync("FT-001");

        Assert.Equal(TransferReferenceLookupStatus.Unavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("FT-001/../../etc/passwd")]
    [InlineData("FT-001?admin=true&extra=1")]
    [InlineData("FT-001#fragment")]
    public async Task GetTransactionByReferenceAsync_ReferenceWithReservedCharacters_IsPercentEncodedNotInterpreted(string reference)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var sut = CreateSut(handler);

        await sut.GetTransactionByReferenceAsync(reference);

        var requestUri = handler.Requests.Single().RequestUri!;
        // The whole reference must land in one path segment under the configured lookup path —
        // never open a new path segment, a query string, or a fragment of its own.
        Assert.StartsWith("/api/transactions/reference/FT-001", requestUri.AbsolutePath);
        Assert.DoesNotContain("/etc/passwd", requestUri.AbsolutePath);
        Assert.Empty(requestUri.Query);
        Assert.Empty(requestUri.Fragment);
    }
}
