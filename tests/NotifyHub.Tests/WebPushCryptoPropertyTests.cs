using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using NotifyHub.Channels;

namespace NotifyHub.Tests;

/// <summary>
/// Property-based tests (FsCheck) for the from-scratch RFC 8291/8292 implementation. The
/// example-based tests next door check one payload and one JWT; these check the invariants
/// against hundreds of generated payloads, audiences and subjects - empty, binary, unicode.
/// </summary>
public class WebPushCryptoPropertyTests
{
    [Property(MaxTest = 100)]
    public bool Any_plaintext_encrypts_to_a_well_formed_aes128gcm_body_that_decrypts_back(byte[] plaintext)
    {
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParams = subscriber.ExportParameters(true);
        var subscriberPublicRaw = UncompressedPoint(subscriberParams.Q.X!, subscriberParams.Q.Y!);
        var authSecret = RandomNumberGenerator.GetBytes(16);

        var body = WebPushCrypto.EncryptPayload(plaintext, Base64UrlEncode(subscriberPublicRaw), Base64UrlEncode(authSecret));

        // RFC 8188 header: salt(16) rs(4) idlen(1) keyid(65); then plaintext + delimiter octet + 16-byte tag.
        if (body.Length != 86 + plaintext.Length + 1 + 16)
            return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4)) != 4096 || body[20] != 65 || body[21] != 0x04)
            return false;

        return Decrypt(body, subscriber, subscriberPublicRaw, authSecret).SequenceEqual(plaintext);
    }

    [Property(MaxTest = 30)]
    public bool Encrypting_the_same_plaintext_twice_never_repeats_salt_ephemeral_key_or_ciphertext(byte[] plaintext)
    {
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParams = subscriber.ExportParameters(false);
        var p256dh = Base64UrlEncode(UncompressedPoint(subscriberParams.Q.X!, subscriberParams.Q.Y!));
        var auth = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        var first = WebPushCrypto.EncryptPayload(plaintext, p256dh, auth);
        var second = WebPushCrypto.EncryptPayload(plaintext, p256dh, auth);

        return !first.AsSpan(0, 16).SequenceEqual(second.AsSpan(0, 16))
            && !first.AsSpan(21, 65).SequenceEqual(second.AsSpan(21, 65))
            && !first.AsSpan(86).SequenceEqual(second.AsSpan(86));
    }

    [Property(MaxTest = 50)]
    public bool The_vapid_jwt_verifies_under_the_public_key_and_carries_audience_and_subject_verbatim(NonNull<string> audience, NonNull<string> subject)
    {
        if (!IsWellFormed(audience.Get) || !IsWellFormed(subject.Get))
            return true;

        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
        var jwt = WebPushCrypto.CreateVapidJwt(audience.Get, subject.Get, publicKey, privateKey);

        var parts = jwt.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || !p.All(IsBase64UrlChar)))
            return false;

        var publicKeyRaw = Base64UrlDecode(publicKey);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKeyRaw[1..33], Y = publicKeyRaw[33..65] },
        });
        var verified = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        return verified
            && payload.GetProperty("aud").GetString() == audience.Get
            && payload.GetProperty("sub").GetString() == subject.Get;
    }

    [Property(MaxTest = 30)]
    public bool Generated_vapid_keys_are_a_65_byte_uncompressed_point_and_a_32_byte_scalar_in_base64url()
    {
        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
        var publicRaw = Base64UrlDecode(publicKey);
        return publicKey.All(IsBase64UrlChar) && privateKey.All(IsBase64UrlChar)
            && publicRaw.Length == 65 && publicRaw[0] == 0x04
            && Base64UrlDecode(privateKey).Length == 32;
    }

    // Manual RFC 8291 decryption without any NotifyHub code, same path as WebPushCryptoTests.
    private static byte[] Decrypt(byte[] body, ECDiffieHellman subscriber, byte[] subscriberPublicRaw, byte[] authSecret)
    {
        var salt = body[..16];
        var keyIdLength = body[20];
        var ephemeralPublicRaw = body[21..(21 + keyIdLength)];
        var ciphertextWithTag = body[(21 + keyIdLength)..];

        using var ephemeralPublic = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = ephemeralPublicRaw[1..33], Y = ephemeralPublicRaw[33..65] },
        });
        var ecdhSecret = subscriber.DeriveRawSecretAgreement(ephemeralPublic.PublicKey);

        var authPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ecdhSecret, salt: authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info"), [0x00], subscriberPublicRaw, ephemeralPublicRaw);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, authPrk, 32, keyInfo);

        var contentPrk = HKDF.Extract(HashAlgorithmName.SHA256, ikm: ikm, salt: salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 16, Concat(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), [0x00]));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, contentPrk, 12, Concat(Encoding.ASCII.GetBytes("Content-Encoding: nonce"), [0x00]));

        var ciphertext = ciphertextWithTag[..^16];
        var tag = ciphertextWithTag[^16..];
        var padded = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(cek, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, padded);

        return padded[^1] == 0x02 ? padded[..^1] : [];
    }

    private static byte[] UncompressedPoint(byte[] x, byte[] y)
    {
        var point = new byte[65];
        point[0] = 0x04;
        Array.Copy(x, 0, point, 1 + (32 - x.Length), x.Length);
        Array.Copy(y, 0, point, 33 + (32 - y.Length), y.Length);
        return point;
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

    private static bool IsWellFormed(string value) =>
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value)) == value;

    private static bool IsBase64UrlChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
