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

    /// <summary>Thrown when the OAuth state parameter returned in the callback does not match the sent state (CSRF guard).</summary>
    public static readonly string STATE_MISMATCH = "STATE_MISMATCH";

    /// <summary>Thrown when the 120-second browser authentication window expires before a callback is received.</summary>
    public static readonly string LOGIN_TIMEOUT = "LOGIN_TIMEOUT";

    /// <summary>Thrown when the requested trace session UUID or name does not match any session.</summary>
    public static readonly string SESSION_NOT_FOUND = "SESSION_NOT_FOUND";

    /// <summary>
    /// Thrown when a command resolves by --name but multiple sessions share that name.
    /// The user must re-issue with --id.
    /// </summary>
    public static readonly string SESSION_AMBIGUOUS = "SESSION_AMBIGUOUS";

    /// <summary>
    /// Thrown by <c>trace session start</c> when no --export-path is given and
    /// no default export path has been configured via <c>trace config set</c>.
    /// </summary>
    public static readonly string EXPORT_PATH_NOT_SET = "EXPORT_PATH_NOT_SET";

    /// <summary>Used by the global exception handler for unhandled non-ZapiCliException errors.</summary>
    public static readonly string INTERNAL_ERROR = "INTERNAL_ERROR";
}
