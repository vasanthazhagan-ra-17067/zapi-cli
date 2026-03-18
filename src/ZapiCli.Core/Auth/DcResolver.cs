namespace ZapiCli.Core.Auth;

/// <summary>
/// Maps Zoho datacenter short names to their Zoho Accounts base URLs (ADR-0002).
/// </summary>
internal static class DcResolver
{
    /// <summary>
    /// Returns the Zoho Accounts base URL for the given datacenter short name.
    /// </summary>
    /// <param name="dc">Short name: us | eu | in | au | cn | jp | sa | uk | ca</param>
    /// <returns>Base URL, e.g. <c>https://accounts.zoho.com</c></returns>
    /// <exception cref="ZapiCliException">Thrown with <c>INVALID_ARGS</c> for unknown dc values.</exception>
    public static string GetAccountsBaseUrl(string dc) => dc switch
    {
        "us" => "https://accounts.zoho.com",
        "eu" => "https://accounts.zoho.eu",
        "in" => "https://accounts.zoho.in",
        "au" => "https://accounts.zoho.com.au",
        "cn" => "https://accounts.zoho.com.cn",
        "jp" => "https://accounts.zoho.jp",
        "sa" => "https://accounts.zoho.sa",
        "uk" => "https://accounts.zoho.uk",
        "ca" => "https://accounts.zohocloud.ca",
        _ => throw new ZapiCliException(
            $"Unknown datacenter '{dc}'. Valid values: us, eu, in, au, cn, jp, sa, uk, ca.",
            ErrorCodes.INVALID_ARGS,
            exitCode: 1),
    };
}
