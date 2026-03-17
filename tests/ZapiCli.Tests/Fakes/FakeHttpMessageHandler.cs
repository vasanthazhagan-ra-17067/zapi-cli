using System.Net;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Configurable <see cref="HttpMessageHandler"/> stub for unit-testing code that depends on
/// <see cref="HttpClient"/> without making real network calls.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    /// <summary>Number of times <see cref="SendAsync"/> was invoked.</summary>
    public int CallCount { get; private set; }

    /// <summary>The last request captured by <see cref="SendAsync"/> (null if never called).</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string responseBody = "{}")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
