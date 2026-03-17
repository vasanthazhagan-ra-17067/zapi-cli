namespace ZapiCli.Core.Auth;

/// <summary>
/// Fetches user information from the Zoho OAuth user-info endpoint.
/// Used during account add to resolve the email before enforcing the ZohoCorp block.
/// </summary>
public interface IUserInfoService
{
    /// <summary>
    /// Returns the email address associated with <paramref name="token"/> on the given
    /// <paramref name="domain"/>, or <c>null</c> if the response does not include an email.
    /// </summary>
    Task<string?> GetEmailAsync(string domain, string token, CancellationToken ct = default);
}
