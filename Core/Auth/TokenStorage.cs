using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentSpotify.Core.Auth;

/// <param name="Scope">Выданные скоупы через пробел; null — токен сохранён старой версией приложения.</param>
public record AuthTokens(string AccessToken, string RefreshToken, DateTime ExpiresAt, string? Scope = null)
{
    /// <summary>true/false — скоуп выдан или нет; null — неизвестно (токен из старой версии).</summary>
    public bool? HasScope(string scope) =>
        Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
}

public static class TokenStorage
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluentSpotify"
    );

    private static readonly string TokenFilePath = Path.Combine(StorageDirectory, "tokens.dat");

    public static async Task SaveTokensAsync(AuthTokens tokens)
    {
        Directory.CreateDirectory(StorageDirectory);

        string json = JsonSerializer.Serialize(tokens);
        byte[] rawBytes = Encoding.UTF8.GetBytes(json);

        // Шифрование данных через DPAPI с привязкой к учетной записи Windows
        byte[] encryptedBytes = ProtectedData.Protect(
            rawBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser
        );

        await File.WriteAllBytesAsync(TokenFilePath, encryptedBytes);
    }

    public static async Task<AuthTokens?> LoadTokensAsync()
    {
        if (!File.Exists(TokenFilePath))
        {
            return null;
        }

        try
        {
            byte[] encryptedBytes = await File.ReadAllBytesAsync(TokenFilePath);

            byte[] rawBytes = ProtectedData.Unprotect(
                encryptedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            string json = Encoding.UTF8.GetString(rawBytes);
            return JsonSerializer.Deserialize<AuthTokens>(json);
        }
        catch
        {
            ClearTokens();
            return null;
        }
    }

    public static void ClearTokens()
    {
        if (File.Exists(TokenFilePath))
        {
            File.Delete(TokenFilePath);
        }
    }
}