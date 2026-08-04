using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NotifyHub.Channels;

/// <summary>
/// A from-scratch implementation of the Web Push Protocol following the current standards - no
/// external crypto library. Two standards are relevant here:
///
/// - RFC 8292 (VAPID): Authorization header in the format "vapid t=&lt;jwt&gt;, k=&lt;publicKey&gt;".
/// - RFC 8291/8188 (aes128gcm): payload encryption with salt/record size/key ID embedded directly
///   in the message body instead of separate headers.
///
/// Older push services (Google FCM, Mozilla) also accept the old draft format for backward
/// compatibility ("Authorization: WebPush ...", separate Crypto-Key/Encryption headers,
/// Content-Encoding "aesgcm") - but Apple's web push implementation follows strictly only the
/// final RFCs and rejects the old format with "BadJwtToken". This class therefore implements
/// exclusively the new, universally supported format.
/// </summary>
internal static class WebPushCrypto
{
    private const int Rfc8188RecordSize = 4096;

    public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(true);

        var point = new byte[65];
        point[0] = 0x04;
        PadLeft(parameters.Q.X!, 32).CopyTo(point, 1);
        PadLeft(parameters.Q.Y!, 32).CopyTo(point, 33);

        return (Base64UrlEncode(point), Base64UrlEncode(PadLeft(parameters.D!, 32)));
    }

    /// <summary>Creates the VAPID JWT (RFC 8292) for a specific push endpoint.</summary>
    public static string CreateVapidJwt(string audience, string subject, string publicKeyB64Url, string privateKeyB64Url)
    {
        var publicKeyBytes = Base64UrlDecode(publicKeyB64Url);
        var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = PadLeft(publicKeyBytes[1..33], 32),
                Y = PadLeft(publicKeyBytes[33..65], 32),
            },
            D = PadLeft(Base64UrlDecode(privateKeyB64Url), 32),
        });
        using var _ = ecdsa;

        var exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
        var headerSegment = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));
        var payloadSegment = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { aud = audience, exp, sub = subject })));
        var signingInput = $"{headerSegment}.{payloadSegment}";

        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    /// <summary>Encrypts the payload per RFC 8291 (Content-Encoding: aes128gcm) for a browser
    /// subscriber (p256dh public key + auth secret from the PushSubscription).</summary>
    public static byte[] EncryptPayload(byte[] plaintext, string p256dhB64Url, string authB64Url)
    {
        var subscriberPublicRaw = Base64UrlDecode(p256dhB64Url);
        var authSecret = Base64UrlDecode(authB64Url);

        using var subscriberEcdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = PadLeft(subscriberPublicRaw[1..33], 32),
                Y = PadLeft(subscriberPublicRaw[33..65], 32),
            },
        });

        using var ephemeralEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralParams = ephemeralEcdh.ExportParameters(false);
        var ephemeralPublicRaw = new byte[65];
        ephemeralPublicRaw[0] = 0x04;
        PadLeft(ephemeralParams.Q.X!, 32).CopyTo(ephemeralPublicRaw, 1);
        PadLeft(ephemeralParams.Q.Y!, 32).CopyTo(ephemeralPublicRaw, 33);

        var ecdhSecret = ephemeralEcdh.DeriveRawSecretAgreement(subscriberEcdh.PublicKey);

        // RFC 8291 section 3.4: first "authenticate" the ECDH secret with the auth secret,
        // then derive the actual key material (IKM) for RFC 8188 from it.
        var authPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ecdhSecret, salt: authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info"), [0x00], subscriberPublicRaw, ephemeralPublicRaw);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, authPrk, 32, keyInfo);

        var salt = RandomNumberGenerator.GetBytes(16);
        var contentPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ikm, salt: salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 16, Concat(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), [0x00]));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 12, Concat(Encoding.ASCII.GetBytes("Content-Encoding: nonce"), [0x00]));

        // Padding delimiter octet (0x02 = last/only record, no further padding needed).
        var padded = Concat(plaintext, [0x02]);
        var ciphertext = new byte[padded.Length];
        var tag = new byte[16];
        using var aesGcm = new AesGcm(cek, tag.Length);
        aesGcm.Encrypt(nonce, padded, ciphertext, tag);

        var header = new byte[16 + 4 + 1 + 65];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), Rfc8188RecordSize);
        header[20] = 65;
        ephemeralPublicRaw.CopyTo(header, 21);

        return Concat(header, ciphertext, tag);
    }

    private static byte[] PadLeft(byte[] data, int length)
    {
        if (data.Length == length)
            return data;
        if (data.Length > length)
            return data[(data.Length - length)..];

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
