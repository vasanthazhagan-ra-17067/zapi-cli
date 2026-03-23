using ZapiCli.Core;
using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Test subclass of <see cref="LocalCallbackServer"/> that bypasses HttpListener binding.
/// Returns preset code+state without any network activity.
/// </summary>
internal sealed class FakeLocalCallbackServer : LocalCallbackServer
{
    private readonly string? _code;
    private readonly string? _state;
    private readonly ZapiCliException? _exceptionToThrow;

    /// <summary>Creates a fake server that returns the preset code and state.</summary>
    public FakeLocalCallbackServer(string code, string state) : base(port: 54321)
    {
        _code = code;
        _state = state;
    }

    /// <summary>Creates a fake server that throws the given exception when awaited.</summary>
    public FakeLocalCallbackServer(ZapiCliException exceptionToThrow) : base(port: 54321)
    {
        _exceptionToThrow = exceptionToThrow;
    }

    public override Task<(string Code, string State)> WaitForCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        return Task.FromResult((_code!, _state!));
    }

    private ZapiCliException? _scopeEnhancedExceptionToThrow;

    /// <summary>Configures the fake server to throw the given exception from WaitForScopeEnhancedCallbackAsync.</summary>
    public void SetScopeEnhancedError(ZapiCliException ex) => _scopeEnhancedExceptionToThrow = ex;

    public override Task WaitForScopeEnhancedCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (_scopeEnhancedExceptionToThrow is not null)
            throw _scopeEnhancedExceptionToThrow;

        return Task.CompletedTask;
    }

    public override void Dispose() { /* nothing to dispose — no real HttpListener */ }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
