using System.Security.Cryptography;
using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Test implementation of <see cref="IRsaKeyPairProvider"/> that generates a real RSA 2048-bit
/// key pair once at construction time and returns it deterministically on every <see cref="Generate"/> call.
/// Also exposes <see cref="EncryptAsServer"/> to let tests simulate what the Zoho server does
/// when it encrypts the <c>client_secret</c> with the public key before putting it in <c>gt_sec</c>.
/// </summary>
internal sealed class FakeRsaKeyPairProvider : IRsaKeyPairProvider, IDisposable
{
    private readonly RSA _rsa;
    private readonly string _publicKeyBase64;

    public FakeRsaKeyPairProvider()
    {
        _rsa = RSA.Create(2048);
        _publicKeyBase64 = Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());
    }

    /// <inheritdoc />
    public (string PublicKeyBase64, RSA PrivateKey) Generate() => (_publicKeyBase64, _rsa);

    /// <summary>
    /// Simulates the Zoho server side: encrypts <paramref name="plaintext"/> (the real
    /// <c>client_secret</c>) with the public key using PKCS#1 v1.5 padding,
    /// returning a base64-encoded string suitable for use as the <c>gt_sec</c> callback parameter.
    /// </summary>
    public string EncryptAsServer(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToBase64String(_rsa.Encrypt(bytes, RSAEncryptionPadding.Pkcs1));
    }

    public void Dispose() => _rsa.Dispose();
}
