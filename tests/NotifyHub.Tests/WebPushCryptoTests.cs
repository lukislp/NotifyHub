using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NotifyHub.Channels;
using Xunit;

namespace NotifyHub.Tests;

/// <summary>
/// Verifies the RFC 8291/8292 from-scratch implementation independently of the WebPush channel
/// logic: a decryption path rebuilt manually (without any NotifyHub code) must return exactly
/// the original plaintext, and the JWT signature must verify against the public key - this
/// catches exactly the kind of bug that Apple's strict web push validation ("BadJwtToken") would
/// immediately flag, but that could otherwise go unnoticed with Google/Mozilla.
/// </summary>
public class WebPushCryptoTests
{
    [Fact]
    public void EncryptPayload_RoundTrips_ViaManualRfc8291Decryption()
    {
        using var subscriberEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParams = subscriberEcdh.ExportParameters(true);
        var subscriberPublicRaw = BuildUncompressedPoint(subscriberParams.Q.X!, subscriberParams.Q.Y!);
        var authSecret = RandomNumberGenerator.GetBytes(16);

        var plaintext = Encoding.UTF8.GetBytes("""{"title":"Hallo","body":"Welt"}""");
        var body = WebPushCrypto.EncryptPayload(plaintext, Base64UrlEncode(subscriberPublicRaw), Base64UrlEncode(authSecret));

        var salt = body[..16];
        var keyIdLength = body[20];
        var ephemeralPublicRaw = body[21..(21 + keyIdLength)];
        var ciphertextWithTag = body[(21 + keyIdLength)..];

        using var ephemeralPublicEcdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = ephemeralPublicRaw[1..33], Y = ephemeralPublicRaw[33..65] },
        });
        var ecdhSecret = subscriberEcdh.DeriveRawSecretAgreement(ephemeralPublicEcdh.PublicKey);

        var authPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ecdhSecret, salt: authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info"), [0x00], subscriberPublicRaw, ephemeralPublicRaw);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, authPrk, 32, keyInfo);

        var contentPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ikm, salt: salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 16, Concat(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), [0x00]));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 12, Concat(Encoding.ASCII.GetBytes("Content-Encoding: nonce"), [0x00]));

        var ciphertext = ciphertextWithTag[..^16];
        var tag = ciphertextWithTag[^16..];
        var decryptedPadded = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(cek, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, decryptedPadded);

        Assert.Equal(0x02, decryptedPadded[^1]);
        var decrypted = decryptedPadded[..^1];
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void CreateVapidJwt_ProducesSignatureVerifiableWithPublicKey_AndCorrectClaims()
    {
        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();

        var jwt = WebPushCrypto.CreateVapidJwt("https://push.example.com", "mailto:test@example.com", publicKey, privateKey);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var publicKeyRaw = Base64UrlDecode(publicKey);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKeyRaw[1..33], Y = publicKeyRaw[33..65] },
        });

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);
        Assert.Equal(64, signature.Length);
        Assert.True(ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        Assert.Equal("https://push.example.com", payload.GetProperty("aud").GetString());
        Assert.Equal("mailto:test@example.com", payload.GetProperty("sub").GetString());
    }

    private static byte[] BuildUncompressedPoint(byte[] x, byte[] y)
    {
        var point = new byte[65];
        point[0] = 0x04;
        PadLeft(x, 32).CopyTo(point, 1);
        PadLeft(y, 32).CopyTo(point, 33);
        return point;
    }

    private static byte[] PadLeft(byte[] data, int length)
    {
        if (data.Length == length)
            return data;
        var padded = new byte[length];
        Array.Copy(data, 0, padded, length - data.Length, data.Length);
        return padded;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
