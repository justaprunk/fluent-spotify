using System.Security.Cryptography;

namespace FluentSpotify.Core.Updates;

/// <summary>
/// Подпись обновлений ключом владельца, который хранится офлайн (installer/signing-key.ps1).
/// build-installer.ps1 подписывает файл &lt;установщик&gt;.sha256: ECDSA P-256 + SHA-256 над точными
/// байтами файла, подпись в формате IEEE P1363 (r || s, 64 байта), в файле .sig — одна строка base64.
/// Без действительной подписи обновление не устанавливается, даже если SHA-256 совпадает:
/// хеш из того же релиза защищает только от повреждения, но не от подмены релиза.
/// </summary>
public static class UpdateSignature
{
    /// <summary>Открытый ключ (SubjectPublicKeyInfo, base64). Смена ключа отрежет установленные копии от обновлений.</summary>
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE/k+GK3InWhFR++WqkyWqe22cgzhOfUttW2a7hUl3G79YzqC5GcHWyJaESOV4n0lveGYm+ShB8msFoKVMlhmABg==";

    private const int SignatureSize = 64;

    /// <summary>Проверяет подпись (текст файла .sig) над байтами data.</summary>
    public static bool Verify(ReadOnlySpan<byte> data, string signatureBase64)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        if (signature.Length != SignatureSize) return false;

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
