namespace ZapiCli.Core.Security;

/// <summary>
/// Enforces the ZohoCorp account hard-block (ADR-0003).
/// This check is sealed into ZapiCli.Core.Security and has NO runtime override path.
/// Must be called at BOTH account add (Story 4) AND api call/scope commands (Stories 5, 7).
/// </summary>
internal static class ZohoCorpGuard
{
    /// <summary>
    /// Throws <see cref="ZapiCliException"/> with code <c>ACCOUNT_DOMAIN_BLOCKED</c>
    /// if the first DNS label of the email host equals <c>zohocorp</c> (case-insensitive).
    /// </summary>
    /// <remarks>
    /// The single-label check (<c>Split('.')[0]</c>) covers zohocorp.com, zohocorp.eu,
    /// zohocorp.in, zohocorp.com.au, and all future datacenter TLDs automatically —
    /// no explicit TLD list required.
    /// If <paramref name="email"/> is null or contains no '@', this method does nothing.
    /// </remarks>
    public static void AssertNotZohoCorp(string? email)
    {
        if (email is null)
            return;

        var atIndex = email.IndexOf('@');
        if (atIndex < 0)
            return;

        var host = email[(atIndex + 1)..];
        var firstLabel = host.Split('.')[0];

        if (firstLabel.Equals("zohocorp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ZapiCliException(
                "ZohoCorp accounts are not permitted.",
                ErrorCodes.ACCOUNT_DOMAIN_BLOCKED,
                exitCode: 1);
        }
    }
}
