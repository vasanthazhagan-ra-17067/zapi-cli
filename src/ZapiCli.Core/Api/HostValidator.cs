namespace ZapiCli.Core.Api;

/// <summary>
/// Sealed compile-time host allowlist enforcer (ADR-0004).
/// Shared between <see cref="ApiClient"/> and <c>api registry add/update</c> commands.
/// Not overridable at runtime — the allowlist is a compile-time constant.
/// </summary>
public static class HostValidator
{
    private static readonly string[] AllowedHostSuffixes =
    [
        "zoho.com",
        "zoho.eu",
        "zoho.in",
        "zoho.com.au",
        "zohoapis.com",
        "zohoapis.in",
    ];

    /// <summary>
    /// Validates that <paramref name="uri"/>'s host ends with one of the allowed Zoho domain
    /// suffixes using proper boundary matching (prevents 'evilzoho.com' from matching 'zoho.com').
    /// </summary>
    /// <exception cref="ZapiCliException">
    /// Thrown with <see cref="ErrorCodes.HOST_NOT_ALLOWED"/> if the host is not in the allowlist.
    /// </exception>
    public static void ValidateHost(Uri uri)
    {
        var host = uri.Host;
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new ZapiCliException(
            "Target host is not in the allowed Zoho domain list.",
            ErrorCodes.HOST_NOT_ALLOWED,
            exitCode: 1);
    }
}
