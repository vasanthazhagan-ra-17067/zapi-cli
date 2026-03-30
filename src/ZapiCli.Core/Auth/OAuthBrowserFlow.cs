using System.Diagnostics;
using System.Security.Cryptography;

namespace ZapiCli.Core.Auth;

/// <summary>
/// Concrete browser OAuth flow helper: generates CSRF state tokens,
/// builds Zoho authorization URLs, and opens the system browser.
/// </summary>
public sealed class OAuthBrowserFlow : IOAuthBrowserFlow
{
    /// <inheritdoc />
    public string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <inheritdoc />
    public string BuildAuthorizationUrl(
        string baseUrl,
        string clientId,
        string redirectUri,
        string[] scopes,
        string state)
    {
        var encodedClientId = Uri.EscapeDataString(clientId);
        var encodedRedirectUri = Uri.EscapeDataString(redirectUri);
        var encodedScope = Uri.EscapeDataString(string.Join(",", scopes));
        var encodedState = Uri.EscapeDataString(state);

        return $"{baseUrl}/oauth/v2/auth" +
               $"?response_type=code" +
               $"&client_id={encodedClientId}" +
               $"&redirect_uri={encodedRedirectUri}" +
               $"&scope={encodedScope}" +
               $"&state={encodedState}" +
               $"&access_type=offline";
    }

    /// <inheritdoc />
    public string BuildMobileAuthorizationUrl(
        string baseUrl,
        string clientId,
        string redirectUri,
        string[] scopes,
        string state,
        string publicKeyBase64)
    {
        var encodedClientId = Uri.EscapeDataString(clientId);
        var encodedRedirectUri = Uri.EscapeDataString(redirectUri);
        var encodedScope = Uri.EscapeDataString(string.Join(",", scopes));
        var encodedState = Uri.EscapeDataString(state);
        var encodedSsId = Uri.EscapeDataString(publicKeyBase64);

        return $"{baseUrl}/oauth/v2/mobile/auth" +
               $"?response_type=code" +
               $"&client_id={encodedClientId}" +
               $"&redirect_uri={encodedRedirectUri}" +
               $"&scope={encodedScope}" +
               $"&state={encodedState}" +
               $"&access_type=offline" +
               $"&newmobilepage=true" +
               $"&ss_id={encodedSsId}";
    }

    /// <inheritdoc />
    public void OpenBrowser(string url)
    {
        // Always print to stderr first — visible even in piped scenarios and serves as fallback.
        Console.Error.WriteLine($"Opening browser for authentication. If the browser does not open, paste this URL manually:");
        Console.Error.WriteLine(url);

        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", url);
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Silently swallow — the printed URL above is the fallback for manual paste.
        }
    }
}
