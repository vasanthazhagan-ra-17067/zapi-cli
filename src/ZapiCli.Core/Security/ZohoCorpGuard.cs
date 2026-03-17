namespace ZapiCli.Core.Security;

/// <summary>
/// Enforces the unconditional hard-block on ZohoCorp-domain accounts.
/// The detection label is a compile-time constant and cannot be overridden at runtime.
/// See ADR-0005.
/// </summary>
public static class ZohoCorpGuard
{
    // Compile-time constant — detection rule is not configurable at runtime (ADR-0005).
    private const string BlockedSld = "zohocorp";

    /// <summary>
    /// Throws <see cref="ZapiCliException"/> with code <c>ACCOUNT_DOMAIN_BLOCKED</c> if
    /// <paramref name="email"/> belongs to any ZohoCorp datacenter domain
    /// (zohocorp.com, zohocorp.eu, zohocorp.in, zohocorp.com.au, etc.).
    /// Detection is based on the second-level domain label matching <c>zohocorp</c>,
    /// which covers all current and future datacenter TLDs automatically.
    /// </summary>
    public static void AssertNotZohoCorp(string? email)
    {
        if (email is null) return;

        var atIdx = email.IndexOf('@');
        if (atIdx < 0) return;

        var sld = email[(atIdx + 1)..].Split('.')[0];
        if (sld.Equals(BlockedSld, StringComparison.OrdinalIgnoreCase))
            throw new ZapiCliException(
                "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.",
                ErrorCodes.AccountDomainBlocked,
                exitCode: 1);
    }
}
