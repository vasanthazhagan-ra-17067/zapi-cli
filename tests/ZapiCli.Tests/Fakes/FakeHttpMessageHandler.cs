namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Configurable <see cref="HttpMessageHandler"/> for unit tests.
/// Returns the response produced by the provided delegate without any network I/O.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Creates an <see cref="IHttpClientFactory"/> backed by this fake handler.</summary>
    public static IHttpClientFactory ToFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler(responder)));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));
}

/// <summary>Returns the same pre-built <see cref="HttpClient"/> for every <c>CreateClient</c> call.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public FakeHttpClientFactory(HttpClient client) => _client = client;
    public HttpClient CreateClient(string name) => _client;
}
