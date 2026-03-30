using System.Net;
using System.Net.Sockets;

namespace ZapiCli.Core.Auth;

/// <summary>
/// Starts a local HTTP server to receive the OAuth redirect callback.
/// <para>
/// When <paramref name="desiredPort"/> is provided the server binds to that exact port — the caller
/// must register <c>http://localhost:{desiredPort}/callback</c> as a redirect URI in the Zoho
/// Developer Console. When omitted the OS assigns an ephemeral port (useful for tests only;
/// Zoho will reject the redirect unless the port was pre-registered).
/// </para>
/// The caller builds the redirect_uri to pass to Zoho as: <c>http://localhost:{Port}/callback</c> (no trailing slash).
/// The HttpListener prefix uses the root path internally: <c>http://localhost:{Port}/</c>.
/// </summary>
public class LocalCallbackServer : IDisposable, IAsyncDisposable
{
    private readonly HttpListener? _listener;
    private bool _disposed;

    /// <summary>The port the server is listening on.</summary>
    public int Port { get; }

    public LocalCallbackServer(int? desiredPort = null)
    {
        _listener = new HttpListener();

        if (desiredPort.HasValue)
        {
            // Fixed port: bind directly — Zoho must have this port registered.
            try
            {
                _listener.Prefixes.Add($"http://localhost:{desiredPort.Value}/");
                _listener.Start();
                Port = desiredPort.Value;
            }
            catch (HttpListenerException)
            {
                throw new ZapiCliException(
                    $"Port {desiredPort.Value} is already in use. Use --port to specify a different port.",
                    ErrorCodes.IO_ERROR,
                    exitCode: 1);
            }
        }
        else
        {
            // Ephemeral port (tests / advanced use).
            int port = AllocateEphemeralPort();
            try
            {
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                Port = port;
            }
            catch (HttpListenerException)
            {
                // TOCTOU: retry once with a fresh ephemeral port allocation.
                _listener.Prefixes.Clear();
                port = AllocateEphemeralPort();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                Port = port;
            }
        }
    }

    /// <summary>
    /// Protected constructor for test subclasses only — sets <see cref="Port"/> without starting an HttpListener.
    /// </summary>
    protected LocalCallbackServer(int port)
    {
        Port = port;
        _listener = null;
    }

    private static int AllocateEphemeralPort()
    {
        // Bind TcpListener to port 0 to get an OS-assigned port, read it, then release.
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    /// <summary>
    /// Waits for the OAuth browser redirect callback.
    /// </summary>
    /// <param name="timeout">Maximum time to wait before throwing LOGIN_TIMEOUT.</param>
    /// <param name="ct">Optional external cancellation token.</param>
    /// <returns>The <c>(code, state)</c> tuple received from the OAuth redirect.</returns>
    /// <exception cref="ZapiCliException">
    /// Thrown with <see cref="ErrorCodes.STATE_MISMATCH"/> if <c>?error=...</c> is present in the callback.
    /// Thrown with <see cref="ErrorCodes.LOGIN_TIMEOUT"/> if the timeout expires.
    /// </exception>
    public virtual async Task<(string Code, string State)> WaitForCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        HttpListenerContext context;
        try
        {
            context = await _listener!.GetContextAsync()
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ZapiCliException(
                "Browser authentication timed out. Run the command again.",
                ErrorCodes.LOGIN_TIMEOUT,
                exitCode: 1);
        }

        var query = context.Request.QueryString;

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            await RespondAsync(context, BuildErrorHtml(error)).ConfigureAwait(false);
            throw new ZapiCliException(error, ErrorCodes.STATE_MISMATCH, exitCode: 1);
        }

        var code = query["code"];
        var state = query["state"];

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            await RespondAsync(context, BuildErrorHtml("Missing code or state parameter."))
                .ConfigureAwait(false);
            throw new ZapiCliException(
                "OAuth callback was missing 'code' or 'state' parameter.",
                ErrorCodes.INVALID_ARGS,
                exitCode: 1);
        }

        await RespondAsync(context, BuildSuccessHtml()).ConfigureAwait(false);
        return (code, state);
    }

    /// <summary>
    /// Waits for the Zoho scope enhancement redirect callback.
    /// Parses <c>?status=success&amp;scope_enhanced=true</c> from the query string.
    /// </summary>
    /// <param name="timeout">Maximum time to wait before throwing LOGIN_TIMEOUT.</param>
    /// <param name="ct">Optional external cancellation token.</param>
    /// <exception cref="ZapiCliException">
    /// Thrown with <see cref="ErrorCodes.SCOPE_ENHANCE_DENIED"/> if <c>?error=...</c> is present
    /// or if the status/scope_enhanced parameters do not indicate success.
    /// Thrown with <see cref="ErrorCodes.LOGIN_TIMEOUT"/> if the timeout expires.
    /// </exception>
    public virtual async Task WaitForScopeEnhancedCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        HttpListenerContext context;
        try
        {
            context = await _listener!.GetContextAsync()
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ZapiCliException(
                "Browser authentication timed out. Run the command again.",
                ErrorCodes.LOGIN_TIMEOUT,
                exitCode: 1);
        }

        var query = context.Request.QueryString;

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            await RespondAsync(context, BuildErrorHtml(error)).ConfigureAwait(false);
            throw new ZapiCliException(error, ErrorCodes.SCOPE_ENHANCE_DENIED, exitCode: 1);
        }

        var status = query["status"];
        var scopeEnhanced = query["scope_enhanced"];

        if (status != "success" || scopeEnhanced != "true")
        {
            await RespondAsync(context, BuildErrorHtml("Scope enhancement was not approved."))
                .ConfigureAwait(false);
            throw new ZapiCliException(
                "Scope enhancement was not approved.",
                ErrorCodes.SCOPE_ENHANCE_DENIED,
                exitCode: 1);
        }

        await RespondAsync(context, BuildSuccessHtml()).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the Zoho Mobile OAuth redirect callback.
    /// Parses <c>code</c>, <c>state</c>, and optionally <c>gt_hash</c>, <c>gt_sec</c>,
    /// <c>accounts-server</c>, and <c>location</c> from the query string.
    /// Only <c>code</c> and <c>state</c> are required; the rest are Zoho client-type-dependent.
    /// </summary>
    /// <param name="timeout">Maximum time to wait before throwing LOGIN_TIMEOUT.</param>
    /// <param name="ct">Optional external cancellation token.</param>
    /// <returns>A <see cref="MobileCallbackResult"/> with all redirect parameters.</returns>
    /// <exception cref="ZapiCliException">
    /// Thrown with <see cref="ErrorCodes.STATE_MISMATCH"/> if <c>?error=...</c> is present in the callback.
    /// Thrown with <see cref="ErrorCodes.INVALID_ARGS"/> if <c>code</c> or <c>state</c> are missing.
    /// Thrown with <see cref="ErrorCodes.LOGIN_TIMEOUT"/> if the timeout expires.
    /// </exception>
    public virtual async Task<MobileCallbackResult> WaitForMobileCallbackAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        HttpListenerContext context;
        try
        {
            context = await _listener!.GetContextAsync()
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ZapiCliException(
                "Browser authentication timed out. Run the command again.",
                ErrorCodes.LOGIN_TIMEOUT,
                exitCode: 1);
        }

        var query = context.Request.QueryString;

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            await RespondAsync(context, BuildErrorHtml(error)).ConfigureAwait(false);
            throw new ZapiCliException(error, ErrorCodes.STATE_MISMATCH, exitCode: 1);
        }

        var code = query["code"];
        var state = query["state"];
        var gtHash = query["gt_hash"];
        var gtSec = query["gt_sec"];
        var accountsServer = query["accounts-server"];
        var location = query["location"];

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            var received = new System.Collections.Generic.List<string>();
            var missing = new System.Collections.Generic.List<string>();
            void Check(string name, string? val) { if (!string.IsNullOrEmpty(val)) received.Add(name); else missing.Add(name); }
            Check("code", code); Check("state", state);

            var diagnostic = $"Mobile OAuth callback was missing required parameters. " +
                $"Received: [{string.Join(", ", received)}]. Missing: [{string.Join(", ", missing)}].";

            await RespondAsync(context, BuildErrorHtml(diagnostic)).ConfigureAwait(false);
            throw new ZapiCliException(diagnostic, ErrorCodes.INVALID_ARGS, exitCode: 1);
        }

        await RespondAsync(context, BuildSuccessHtml()).ConfigureAwait(false);
        return new MobileCallbackResult(code, state, gtHash, gtSec, accountsServer, location);
    }

    private static async Task RespondAsync(HttpListenerContext context, string html)
    {
        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
        response.Close();
    }

    private static string BuildSuccessHtml() =>
        "<!DOCTYPE html><html><head><title>Authentication successful</title></head>" +
        "<body><h1>Authentication successful!</h1><p>You can close this tab.</p></body></html>";

    private static string BuildErrorHtml(string error) =>
        "<!DOCTYPE html><html><head><title>Authentication error</title></head>" +
        $"<body><h1>Authentication error</h1><p>{System.Net.WebUtility.HtmlEncode(error)}</p></body></html>";

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener?.Stop(); } catch (ObjectDisposedException) { }
        try { _listener?.Close(); } catch (ObjectDisposedException) { }
    }

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
