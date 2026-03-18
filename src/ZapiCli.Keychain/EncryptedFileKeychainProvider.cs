using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ZapiCli.Keychain;

/// <summary>
/// AES-256-GCM encrypted file fallback implementation of <see cref="IKeychainProvider"/>.
/// Used on headless Linux servers, CI runners, or whenever the OS keychain is unavailable.
///
/// Key derivation: SHA-256(MachineName + MachineGuid + "zapi-cli").
/// File layout per secret: first 12 bytes = IV, remainder = ciphertext + 16-byte GCM tag.
/// Files stored at: <c>&lt;configDir&gt;/keystore/&lt;sanitized-key&gt;.bin</c>.
/// </summary>
public sealed class EncryptedFileKeychainProvider : IKeychainProvider
{
    private readonly string _keystoreDir;
    private readonly byte[] _key;

    public EncryptedFileKeychainProvider(string configDir)
    {
        _keystoreDir = Path.Combine(configDir, "keystore");
        _key = DeriveKey();
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path))
            return Task.FromResult<string?>(null);

        var fileBytes = File.ReadAllBytes(path);
        if (fileBytes.Length < 12 + 16)
            return Task.FromResult<string?>(null);

        var iv = fileBytes[..12];
        var ciphertext = fileBytes[12..];

        // ciphertext includes the 16-byte tag at the end
        var tag = ciphertext[^16..];
        var encryptedData = ciphertext[..^16];
        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(iv, encryptedData, tag, plaintext);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_keystoreDir);

        var plaintext = Encoding.UTF8.GetBytes(value);
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(iv, plaintext, ciphertext, tag);

        // Layout: [12-byte IV][ciphertext][16-byte tag]
        var fileBytes = new byte[12 + ciphertext.Length + 16];
        iv.CopyTo(fileBytes, 0);
        ciphertext.CopyTo(fileBytes, 12);
        tag.CopyTo(fileBytes, 12 + ciphertext.Length);

        var path = GetFilePath(key);
        File.WriteAllBytes(path, fileBytes);

        // Restrict file to owner only on Unix
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (PlatformNotSupportedException) { }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = GetFilePath(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for use as filename: replace ':' with '_'
        var safeName = key.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        return Path.Combine(_keystoreDir, $"{safeName}.bin");
    }

    private static byte[] DeriveKey()
    {
        var machineName = Environment.MachineName ?? string.Empty;
        var machineGuid = GetMachineGuid();
        var combined = Encoding.UTF8.GetBytes(machineName + machineGuid + "zapi-cli");
        return SHA256.HashData(combined);
    }

    private static string GetMachineGuid()
    {
        if (OperatingSystem.IsWindows())
            return GetWindowsMachineGuid();
        if (OperatingSystem.IsMacOS())
            return GetMacOsMachineGuid();
        return GetLinuxMachineId();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string GetWindowsMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetMacOsMachineGuid()
    {
        try
        {
            var psi = new ProcessStartInfo("ioreg", "-rd1 -c IOPlatformExpertDevice")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return string.Empty;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            // Parse: "IOPlatformUUID" = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            var marker = "\"IOPlatformUUID\" = \"";
            var idx = output.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return string.Empty;
            var start = idx + marker.Length;
            var end = output.IndexOf('"', start);
            return end > start ? output[start..end] : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLinuxMachineId()
    {
        try
        {
            return File.ReadAllText("/etc/machine-id").Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Returns the platform-specific config directory for zapi-cli.</summary>
    public static string GetDefaultConfigDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "zapi-cli");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "zapi-cli");

        // Linux: ~/.config/zapi-cli
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "zapi-cli");
    }
}
