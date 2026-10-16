using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotifyAPI.Web;

namespace FluentSpotify.Core.Auth;

/// <summary>
/// Авторизация Spotify по Authorization Code + PKCE (без client secret).
/// </summary>
public class PkceAuthService
{
    // Spotify не принимает "localhost" в redirect URI — только loopback-адрес 127.0.0.1
    public const string RedirectUri = "http://127.0.0.1:5000/callback/";
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string PlaceholderClientId = "YOUR_SPOTIFY_CLIENT_ID";
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    // Скоупы для управления плеером, библиотекой и плейлистами
    private static readonly string[] RequiredScopes =
    [
        Scopes.UserReadPlaybackState,
        Scopes.UserModifyPlaybackState,
        Scopes.UserReadCurrentlyPlaying,
        Scopes.PlaylistReadPrivate,
        Scopes.PlaylistReadCollaborative,
        Scopes.UserLibraryRead,
        Scopes.UserTopRead
    ];

    private string _clientId;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public PkceAuthService(string clientId)
    {
        _clientId = clientId;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && _clientId != PlaceholderClientId;

    /// <summary>Задать Client ID во время работы (например, введённый пользователем на экране входа).</summary>
    public void SetClientId(string clientId)
    {
        _clientId = clientId.Trim();
    }

    public async Task<AuthTokens> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Не задан Spotify Client ID. Введите его на экране входа.");
        }

        var (verifier, challenge) = GeneratePkceCodes();
        string state = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        var loginRequest = new LoginRequest(
            new Uri(RedirectUri),
            _clientId,
            LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            State = state,
            Scope = RequiredScopes
        };

        // Поднимаем временный HTTP-сервер для перехвата редиректа
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        // Открываем браузер по умолчанию
        Process.Start(new ProcessStartInfo
        {
            FileName = loginRequest.ToUri().ToString(),
            UseShellExecute = true
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(LoginTimeout);
        using var registration = timeoutCts.Token.Register(() => listener.Stop());

        string? code = null;
        string? error = null;
        try
        {
            // Ждём именно наш callback: посторонние запросы (favicon и т.п.) отклоняем
            while (true)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;

                if (request.Url?.AbsolutePath.TrimEnd('/') != "/callback")
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                bool stateValid = request.QueryString["state"] == state;
                code = stateValid ? request.QueryString["code"] : null;
                error = stateValid ? request.QueryString["error"] : "state_mismatch";

                bool success = !string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error);
                await WriteBrowserResponseAsync(context.Response, success);
                break;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Авторизация в браузере не была завершена.");
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException($"Ошибка авторизации Spotify: {error ?? "отсутствует код авторизации"}");
        }

        // Обмениваем полученный auth code на токены
        return await ExchangeCodeForTokensAsync(code, verifier);
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success)
    {
        string message = success
            ? "<h2>Fluent Spotify авторизован!</h2><p>Можете закрыть эту вкладку и вернуться в приложение.</p>"
            : "<h2>Не удалось авторизоваться</h2><p>Вернитесь в приложение и попробуйте ещё раз.</p>";

        byte[] responseBytes = Encoding.UTF8.GetBytes(
            "<html><head><meta charset='utf-8'></head>" +
            "<body style='font-family:sans-serif;text-align:center;padding:50px;background:#181818;color:#fff;'>" +
            message + "</body></html>");

        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = responseBytes.Length;
        await response.OutputStream.WriteAsync(responseBytes);
        response.OutputStream.Close();
    }

    public async Task<AuthTokens> RefreshTokenAsync(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken }
        };

        using var root = await PostTokenRequestAsync(parameters);

        string newAccessToken = root.RootElement.GetProperty("access_token").GetString()!;
        int expiresIn = root.RootElement.GetProperty("expires_in").GetInt32();

        // Spotify может не возвращать новый refresh_token — тогда сохраняем существующий
        string returnedRefreshToken =
            root.RootElement.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } newRt
                ? newRt
                : refreshToken;

        var tokens = new AuthTokens(newAccessToken, returnedRefreshToken, DateTime.UtcNow.AddSeconds(expiresIn));
        await TokenStorage.SaveTokensAsync(tokens);
        return tokens;
    }

    private async Task<AuthTokens> ExchangeCodeForTokensAsync(string code, string verifier)
    {
        var parameters = new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", RedirectUri },
            { "code_verifier", verifier }
        };

        using var root = await PostTokenRequestAsync(parameters);

        var tokens = new AuthTokens(
            root.RootElement.GetProperty("access_token").GetString()!,
            root.RootElement.GetProperty("refresh_token").GetString()!,
            DateTime.UtcNow.AddSeconds(root.RootElement.GetProperty("expires_in").GetInt32()));

        await TokenStorage.SaveTokensAsync(tokens);
        return tokens;
    }

    private async Task<JsonDocument> PostTokenRequestAsync(Dictionary<string, string> parameters)
    {
        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters));
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // invalid_grant = refresh-токен отозван/просрочен: нужна повторная авторизация
            bool invalidGrant = body.Contains("invalid_grant", StringComparison.Ordinal);
            throw new SpotifyAuthException($"Spotify token endpoint returned {(int)response.StatusCode}: {body}", invalidGrant);
        }

        return JsonDocument.Parse(body);
    }

    private static (string Verifier, string Challenge) GeneratePkceCodes()
    {
        string verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}

public sealed class SpotifyAuthException : Exception
{
    public bool RequiresReauthentication { get; }

    public SpotifyAuthException(string message, bool requiresReauthentication) : base(message)
    {
        RequiresReauthentication = requiresReauthentication;
    }
}
