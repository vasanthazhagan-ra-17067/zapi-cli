using System.Security.AccessControl;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Reads and writes <c>accounts.json</c> in the user's config directory.
/// Token values are never stored here — they live in the OS keychain.
/// After every write, Unix file permissions are set to 0600 and Windows ACL
/// is restricted to the current user (OQ-004).
/// </summary>
public sealed class AccountStore : IAccountStore
{
    private readonly ILogger<AccountStore> _logger;
    private readonly string _configDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AccountStore(ILogger<AccountStore> logger)
        : this(logger, ResolveConfigDir()) { }

    /// <summary>Internal overload used by unit tests to inject a temp config directory.</summary>
    internal AccountStore(ILogger<AccountStore> logger, string configDir)
    {
        _logger = logger;
        _configDir = configDir;
    }

    private string AccountsFilePath => Path.Combine(_configDir, "accounts.json");

    private static string ResolveConfigDir() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zapi-cli")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "zapi-cli");

    public async Task<AccountsRoot> LoadAsync(CancellationToken ct = default)
    {
        var path = AccountsFilePath;
        if (!File.Exists(path))
            return new AccountsRoot();

        var json = await File.ReadAllTextAsync(path, ct);
        var root = JsonSerializer.Deserialize<AccountsRoot>(json, JsonOptions) ?? new AccountsRoot();

        foreach (var account in root.Accounts)
        {
            if (account.Email is not null && IsZohoCorpDomain(account.Email))
                _logger.LogWarning(
                    "Account '{Name}' has a ZohoCorp-domain email address and will be blocked on use.",
                    account.Name);
        }

        return root;
    }

    public async Task SaveAsync(AccountsRoot root, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_configDir);
        var path = AccountsFilePath;
        var json = JsonSerializer.Serialize(root, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
        ApplyFilePermissions(path);
    }

    public async Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        return root.Accounts.Find(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        return root.Accounts.Find(a => a.IsDefault)
               ?? throw new ZapiCliException(
                   "No default account configured. Use 'account set-default <name>' to set one.",
                   ErrorCodes.NoDefaultAccount,
                   exitCode: 1);
    }

    private void ApplyFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            TrySetWindowsAcl(path);
        else
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void TrySetWindowsAcl(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                Environment.UserName,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restrict file permissions on '{Path}' to current user.", path);
        }
    }

    private static bool IsZohoCorpDomain(string email)
    {
        var atIdx = email.IndexOf('@');
        if (atIdx < 0) return false;
        return email[(atIdx + 1)..].Split('.')[0]
            .Equals("zohocorp", StringComparison.OrdinalIgnoreCase);
    }
}
