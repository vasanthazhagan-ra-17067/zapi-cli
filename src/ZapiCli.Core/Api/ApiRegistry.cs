using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Api;

/// <summary>
/// Reads and writes <c>registry.json</c> inside the platform config directory.
/// On macOS/Linux the file is written with 0600 permissions.
/// On Windows the DACL is set to restrict access to the current user only.
/// </summary>
public sealed class ApiRegistry : IApiRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _registryPath;
    private readonly ILogger<ApiRegistry> _logger;

    /// <summary>Testable constructor — accepts an explicit config directory.</summary>
    public ApiRegistry(string configDir, ILogger<ApiRegistry> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(configDir);
        _registryPath = Path.Combine(configDir, "registry.json");
    }

    // ─── IApiRegistry ─────────────────────────────────────────────────────────

    public async Task<ApiRegistryRoot> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_registryPath))
            return new ApiRegistryRoot();

        var json = await File.ReadAllTextAsync(_registryPath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ApiRegistryRoot>(json, JsonOptions) ?? new ApiRegistryRoot();
    }

    public async Task SaveAsync(ApiRegistryRoot root, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(root, JsonOptions);
        await File.WriteAllTextAsync(_registryPath, json, ct).ConfigureAwait(false);
        ApplyPermissions(_registryPath);
    }

    public async Task<ApiRegistryEntry?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);
        return root.Apis.FirstOrDefault(e => e.Id.Equals(id, StringComparison.Ordinal));
    }

    public async Task AddAsync(ApiRegistryEntry entry, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);
        if (root.Apis.Any(e => e.Id.Equals(entry.Id, StringComparison.Ordinal)))
            throw new ZapiCliException(
                $"API registry entry '{entry.Id}' already exists. Use 'api endpoints update --id {entry.Id}' to modify it.",
                ErrorCodes.ENDPOINT_ALREADY_EXISTS);

        root.Apis.Add(entry);
        await SaveAsync(root, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ApiRegistryEntry entry, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);
        var idx = root.Apis.FindIndex(e => e.Id.Equals(entry.Id, StringComparison.Ordinal));
        if (idx < 0)
            throw new ZapiCliException(
                $"API registry entry '{entry.Id}' not found.",
                ErrorCodes.ENDPOINT_NOT_FOUND);

        root.Apis[idx] = entry;
        await SaveAsync(root, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct).ConfigureAwait(false);
        var idx = root.Apis.FindIndex(e => e.Id.Equals(id, StringComparison.Ordinal));
        if (idx < 0)
            throw new ZapiCliException(
                $"API registry entry '{id}' not found.",
                ErrorCodes.ENDPOINT_NOT_FOUND);

        root.Apis.RemoveAt(idx);
        await SaveAsync(root, ct).ConfigureAwait(false);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void ApplyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            ApplyWindowsAcl(path);
        else
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private void ApplyWindowsAcl(string path)
    {
        try
        {
            var fileSecurity = new FileSecurity(
                path,
                AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);

            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null)
            {
                var rule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow);
                fileSecurity.SetAccessRule(rule);
            }

            new FileInfo(path).SetAccessControl(fileSecurity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to restrict NTFS ACL on registry.json. " +
                "The file may be accessible to other users on this machine.");
        }
    }
}
