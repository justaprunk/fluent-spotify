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
    /// <summary>
    /// Открытые ключи (SubjectPublicKeyInfo, base64); подходит подпись любым из них.
    /// Основной ключ подписывает каждый релиз. Резервный хранится офлайн на внешнем носителе
    /// и нужен, если основной утерян или скомпрометирован: релиз, подписанный резервным,
    /// заменяет этот список. Без такого релиза смена ключей отрезала бы установленные копии от обновлений.
    /// </summary>
    private static readonly string[] PublicKeys =
    [
        // Основной: %USERPROFILE%\.fluent-spotify\update-signing-key.dpapi
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE/k+GK3InWhFR++WqkyWqe22cgzhOfUttW2a7hUl3G79YzqC5GcHWyJaESOV4n0lveGYm+ShB8msFoKVMlhmABg==",
        // Резервный: signing-key.ps1 -NewRecovery, только на внешнем носителе
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEssdfnrASzrBUr2oYL0mXhaJwsDaeBy9nmKweZjbRIERy35sv0CACaZdPqQX2CE1T6wRoizaAlmS1oYAbRxv38g=="
    ];

    private const int SignatureSize = 64;

    /// <summary>Проверяет подпись (текст файла .sig) над байтами data.</summary>
    public static bool Verify(ReadOnlySpan<byte> data, string signatureBase64) => Verify(data, signatureBase64, PublicKeys);

    internal static bool Verify(ReadOnlySpan<byte> data, string signatureBase64, IReadOnlyList<string> publicKeys)
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

        foreach (string publicKey in publicKeys)
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return true;
        }
        return false;
    }
}
