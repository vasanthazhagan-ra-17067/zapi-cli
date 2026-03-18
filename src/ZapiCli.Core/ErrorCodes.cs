namespace ZapiCli.Core;

/// <summary>
/// Symbolic error code vocabulary for the JSON error envelope (ADR-0008).
/// Callers branch on these codes; the human-readable message may change independently.
/// </summary>
public static class ErrorCodes
{
    public static readonly string ACCOUNT_NOT_FOUND = "ACCOUNT_NOT_FOUND";
    public static readonly string ACCOUNT_ALREADY_EXISTS = "ACCOUNT_ALREADY_EXISTS";
    public static readonly string NO_DEFAULT_ACCOUNT = "NO_DEFAULT_ACCOUNT";
    public static readonly string AUTH_FAILURE = "AUTH_FAILURE";
    public static readonly string NEEDS_REAUTH = "NEEDS_REAUTH";
    public static readonly string API_ERROR = "API_ERROR";
    public static readonly string INVALID_ARGS = "INVALID_ARGS";
    public static readonly string IO_ERROR = "IO_ERROR";
    public static readonly string KEYCHAIN_ERROR = "KEYCHAIN_ERROR";
    public static readonly string ACCOUNT_DOMAIN_BLOCKED = "ACCOUNT_DOMAIN_BLOCKED";
    public static readonly string EMAIL_REQUIRED = "EMAIL_REQUIRED";
    public static readonly string HOST_NOT_ALLOWED = "HOST_NOT_ALLOWED";

    /// <summary>Used by the global exception handler for unhandled non-ZapiCliException errors.</summary>
    public static readonly string INTERNAL_ERROR = "INTERNAL_ERROR";
}
