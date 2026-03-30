using System.Security.Cryptography;

namespace ZapiCli.Core.Auth;

/// <summary>
/// Generates RSA key pairs for the Zoho Mobile OAuth flow.
/// The public key is sent to Zoho as <c>ss_id</c> in the authorization URL,
/// allowing Zoho to encrypt the <c>client_secret</c> in the redirect callback.
/// The private key is used to decrypt the <c>gt_sec</c> value from the callback.
/// </summary>
public interface IRsaKeyPairProvider
{
    /// <summary>
    /// Generates a fresh RSA key pair.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///   <item><description><c>PublicKeyBase64</c> — the SubjectPublicKeyInfo-encoded public key, base64-encoded.
    ///   This is the value sent as <c>ss_id</c> in the Zoho Mobile OAuth authorization URL.</description></item>
    ///   <item><description><c>PrivateKey</c> — the <see cref="RSA"/> instance holding both keys.
    ///   Used to decrypt the <c>gt_sec</c> parameter from the OAuth redirect callback.</description></item>
    /// </list>
    /// </returns>
    (string PublicKeyBase64, RSA PrivateKey) Generate();
}
