using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoKeepKill;

internal static class LicenseTokenVerifier
{
    private const string Issuer = "https://www.trykeeply.live";
    private const string Audience = "Keeply.Windows";
    private const long AllowedClockSkewSeconds = 300;

    // Public verification key only. The matching private key is kept in the website's
    // server environment and must never be included in the desktop application.
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAy1MTQ9J6E1kkiYSViZES
        d5eohAPWnTicxHJVoge8iyo5jd0bfiMgkJvxmEOn/+NqBQtC05Bcg8nHWWz41hva
        0MwCedn6OcTb76I6tTL0NhPJMslcCxT3ynD8RTbkQ3RE+NQSj2d/nggGGUiGS6/F
        lYj3PiHl2pM8IiNDEeVGpD72zphXeyCnfZI6jrShZSYu5/AiDnROB0Kpf1ZQXNM1
        CSvaiXNmWd6JoXlYT4dmV7H4mKsfYHT2u+S+KVjodYpg7LLiPu0B80Pw6zkzXS8z
        6NCqoIfizTKbz3sMdfUWRuXF9H960Y1hweqiZ2B7khB1/InZXmpS9JceazUcTjLn
        XF5ii9o78HwhI4F1q2Ja0BAYwQFwtgFG5C6xSlduL0iD/DcKgbY1GU/3/+UO3L/j
        DukC9QNSeL5TekcAtd0YR2XHx1SLUAnaOsKdN7m6E0AlPQ6A7Tb1QOKTFPeHaDdr
        D9zxly0Mr3oQjeKuAUY4SXUZ067ecSmnS66G6R1CFcb5AgMBAAE=
        -----END PUBLIC KEY-----
        """;

    public static bool IsValid(string token, string expectedInstanceId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || string.IsNullOrWhiteSpace(expectedInstanceId))
            return false;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return false;

            var headerBytes = DecodeBase64Url(parts[0]);
            using var header = JsonDocument.Parse(headerBytes);
            if (!header.RootElement.TryGetProperty("alg", out var algorithm) || algorithm.GetString() != "RS256" ||
                !header.RootElement.TryGetProperty("typ", out var type) || type.GetString() != "JWT")
                return false;

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            var signedData = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!rsa.VerifyData(signedData, DecodeBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return false;

            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var claims = payload.RootElement;
            if (!claims.TryGetProperty("iss", out var issuer) || issuer.GetString() != Issuer ||
                !claims.TryGetProperty("aud", out var audience) || audience.GetString() != Audience ||
                !claims.TryGetProperty("licenseType", out var licenseType) || licenseType.GetString() != "lifetime" ||
                !claims.TryGetProperty("instanceId", out var instanceId) || instanceId.GetString() != expectedInstanceId ||
                !claims.TryGetProperty("iat", out var issuedAt) || !issuedAt.TryGetInt64(out var issuedAtSeconds))
                return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return issuedAtSeconds > 0 && issuedAtSeconds <= now + AllowedClockSkewSeconds;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += base64.Length % 4 switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException("Invalid base64url value.") };
        return Convert.FromBase64String(base64);
    }
}
