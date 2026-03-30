using System.Security.Cryptography;

namespace ZapiCli.Core.Auth;

/// <summary>
/// Production implementation of <see cref="IRsaKeyPairProvider"/>.
/// Generates a fresh 2048-bit RSA key pair on each call to <see cref="Generate"/>.
/// The returned <see cref="RSA"/> instance is owned by the caller and must be disposed.
/// </summary>
public sealed class RsaKeyPairProvider : IRsaKeyPairProvider
{
    /// <inheritdoc />
    public (string PublicKeyBase64, RSA PrivateKey) Generate()
    {
        var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return (publicKey, rsa);
    }
}
