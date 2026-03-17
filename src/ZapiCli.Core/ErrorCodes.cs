namespace ZapiCli.Core;

public static class ErrorCodes
{
    public static readonly string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public static readonly string AccountAlreadyExists = "ACCOUNT_ALREADY_EXISTS";
    public static readonly string AccountDomainBlocked = "ACCOUNT_DOMAIN_BLOCKED";
    public static readonly string NoDefaultAccount = "NO_DEFAULT_ACCOUNT";
    public static readonly string AuthFailure = "AUTH_FAILURE";
    public static readonly string NeedsReauth = "NEEDS_REAUTH";
    public static readonly string ApiError = "API_ERROR";
    public static readonly string InvalidArgs = "INVALID_ARGS";
    public static readonly string IoError = "IO_ERROR";
    public static readonly string KeychainError = "KEYCHAIN_ERROR";
    public static readonly string HostNotAllowed = "HOST_NOT_ALLOWED";
    public static readonly string NotImplemented = "NOT_IMPLEMENTED";
    public static readonly string SessionNotFound = "SESSION_NOT_FOUND";
    public static readonly string ScopeNotFound = "SCOPE_NOT_FOUND";
    public static readonly string RegistryEntryNotFound = "REGISTRY_ENTRY_NOT_FOUND";
    public static readonly string InternalError = "INTERNAL_ERROR";
}
