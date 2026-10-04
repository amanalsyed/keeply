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
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAuDUsrF6qG1pZkUvnCp2h
        K49Vgcd8kr3msj0cof8QPtZiRIm3k9vYf4GAg0MBVOQhshCNi2W0IDqgTVs+BY6m
        bWOip8h6KVlW32WwyE7EwYulLP9FHcp672yVVdquRtR85v6K6Rw5Gr2CcSdAc3q4
        J2Pgwg0qmnFfCpiN23HkaeiLsl/OSb3OxX4UBSQG4W24sqzRR++D1EE3ssYYPOVh
        LvOsOw4cTEXXEhoS7ClQHXTBIbaegvDkz4OxGMwkd+kQNHzQ2LA7B6W1eXfd4jsW
        MA522XKHVDVQmJGZlm+EsCJhpYbVzWczdBlwi1+O8VitRZA6rwvAQWEDpSns6xSn
        M4x2MXmEORwy4xP4gAOKPTPbM9qRHPTAHQqZWm18m5rP8ALlHDaM8jGlVzUzBZcA
        wHG7ifuRwxlqxEmF/T2E724jEHFMkfU6H/u8gLAjLya1O8tlrS53Rd9Uk+BYJB1G
        iyltMUR1vEaMJ9ur8d4H0AW18yPTN/vlL2HEIplX3Anl/n3ifbxFqn3cs2Y8cueS
        CJVOM0eVLpcGQrBINJfMkGMrkw7a0O0AkQFUBpJsg5ITJmIxCZkf/pDTyXw0cMbC
        CJyRhJxGEoKZrqVqWV0AudMm8OrNehjERCgsUu4TL3KUGKuyRT2LYURY9cLXlOLE
        06R1AlKPiEPF7VpjFT2ZDJUCAwEAAQ==
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
        base64 += (base64.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException("Invalid base64url value.") };
        return Convert.FromBase64String(base64);
    }
}
