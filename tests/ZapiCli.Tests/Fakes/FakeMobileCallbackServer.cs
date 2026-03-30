using ZapiCli.Core;
using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Test subclass of <see cref="LocalCallbackServer"/> for the Zoho Mobile OAuth flow.
/// Bypasses HttpListener binding and returns a preset <see cref="MobileCallbackResult"/>
/// without any network activity.
/// </summary>
internal sealed class FakeMobileCallbackServer : LocalCallbackServer
{
    private readonly MobileCallbackResult? _result;
    private readonly ZapiCliException? _exceptionToThrow;

    /// <summary>Creates a fake server that returns the preset mobile callback result.</summary>
    public FakeMobileCallbackServer(MobileCallbackResult result) : base(port: 54323)
    {
        _result = result;
    }

    /// <summary>Creates a fake server that throws the given exception when awaited.</summary>
    public FakeMobileCallbackServer(ZapiCliException exceptionToThrow) : base(port: 54323)
    {
        _exceptionToThrow = exceptionToThrow;
    }

    public override Task<MobileCallbackResult> WaitForMobileCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        return Task.FromResult(_result!);
    }

    public override void Dispose() { /* nothing to dispose — no real HttpListener */ }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
