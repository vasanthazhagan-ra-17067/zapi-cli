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

    /// <summary>Thrown when an api registry operation targets an id that does not exist.</summary>
    public static readonly string REGISTRY_ENTRY_NOT_FOUND = "REGISTRY_ENTRY_NOT_FOUND";

    /// <summary>Thrown by <c>api registry add</c> when the specified id already exists in the registry.</summary>
    public static readonly string REGISTRY_ENTRY_ALREADY_EXISTS = "REGISTRY_ENTRY_ALREADY_EXISTS";

    /// <summary>
    /// Thrown by <c>scope add</c> when the user rejects the scope enhancement consent,
    /// or when the callback returns an unexpected state.
    /// </summary>
    public const string SCOPE_ENHANCE_DENIED = "SCOPE_ENHANCE_DENIED";

    /// <summary>
    /// Thrown when the keychain credential rename operation fails
    /// (e.g., write succeeded but delete of old key failed).
    /// </summary>
    public static readonly string ACCOUNT_RENAME_FAILED = "ACCOUNT_RENAME_FAILED";

    /// <summary>
    /// Thrown when the keychain credential rename operation fails
    /// (e.g., write succeeded but delete of old key failed).
    /// </summary>
    public static readonly string SCOPE_ENHANCE_FAILED = "SCOPE_ENHANCE_FAILED";

    /// <summary>
    /// Thrown by the Zoho Mobile OAuth flow when the token exchange response does not contain
    /// a <c>dc_locations</c> object. Zoho requires this field for Data Center Location routing;
    /// its absence indicates a mobile app configuration error or an unsupported token type.
    /// </summary>
    public static readonly string DCL_MISSING = "DCL_MISSING";

    /// <summary>
    /// Thrown by the Zoho Mobile OAuth flow when RSA decryption of the <c>gt_sec</c> parameter
    /// (the encrypted <c>client_secret</c> from the OAuth redirect) fails.
    /// </summary>
    public static readonly string RSA_DECRYPT_FAILURE = "RSA_DECRYPT_FAILURE";

    /// <summary>
    /// Thrown when more than one of --name, --email, --zuidstring is provided to a command
    /// that accepts exactly one account identifier.
    /// </summary>
    public static readonly string DUPLICATE_IDENTIFIER = "DUPLICATE_IDENTIFIER";

    /// <summary>
    /// Thrown by <c>account login</c> when <c>ZOHO_CLIENT_ID</c> is not set in the environment.
    /// Configure an env-file via <c>zapi-cli config set env-file &lt;path&gt;</c>.
    /// </summary>
    public static readonly string ENV_FILE_NOT_CONFIGURED = "ENV_FILE_NOT_CONFIGURED";

    /// <summary>
    /// Thrown by <c>account login</c> when no scopes are provided via --scope or a configured scope-file.
    /// Configure a scope file via <c>zapi-cli config set scope-file &lt;path&gt;</c>.
    /// </summary>
    public static readonly string SCOPE_FILE_NOT_CONFIGURED = "SCOPE_FILE_NOT_CONFIGURED";
}
