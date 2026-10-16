using FluentSpotify.Core.Api;
using FluentSpotify.Core.Settings;
using SpotifyAPI.Web;
using Wshm.Common.Diagnostics;

namespace FluentSpotify.Core.Playback;

/// <summary>
/// Режим «официальный клиент в фоне»: при необходимости запускает скрытый клиент Spotify
/// и переносит на него воспроизведение, если сейчас ничего не играет.
/// </summary>
public sealed class BackgroundPlaybackService
{
    private static readonly TimeSpan DeviceWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly SpotifyApiClient _api;
    private readonly SpotifyClientLauncher _launcher;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsEnabled => _settings.Current.LaunchClientInBackground;

    public BackgroundPlaybackService(SpotifyApiClient api, SpotifyClientLauncher launcher, SettingsService settings)
    {
        _api = api;
        _launcher = launcher;
        _settings = settings;

        // Команда плеера упала из-за отсутствия устройства — поднимаем клиент и повторяем
        _api.NoActiveDeviceRecovery = () => EnsurePlaybackDeviceAsync(launchIfNeeded: IsEnabled);
    }

    /// <summary>Вызывается после входа в аккаунт: подготовить устройство заранее.</summary>
    public Task PrepareAsync() =>
        IsEnabled ? EnsurePlaybackDeviceAsync(launchIfNeeded: true) : Task.FromResult(false);

    /// <summary>
    /// Гарантирует, что есть активное устройство. Чужое активное устройство (телефон, другой ПК)
    /// не перехватывается. Возвращает true, если активное устройство есть или появилось.
    /// </summary>
    public async Task<bool> EnsurePlaybackDeviceAsync(bool launchIfNeeded)
    {
        await _lock.WaitAsync();
        try
        {
            var devices = await _api.GetDevicesAsync();
            if (devices.Any(d => d.IsActive))
            {
                return true;
            }

            // Клиент не запущен — поднимаем его, даже если устройство ещё числится в списке:
            // после закрытия Spotify его запись какое-то время остаётся, но она уже «мёртвая»
            bool clientRunning = SpotifyClientLauncher.IsRunning;
            if (!clientRunning && launchIfNeeded)
            {
                clientRunning = await _launcher.EnsureRunningHiddenAsync();
            }

            if (!clientRunning && FindLocalDevice(devices) == null)
            {
                AppLog.Write("BackgroundPlayback", "Local Spotify client is not running");
                return false;
            }

            // Свежезапущенный клиент регистрируется в Connect не сразу — пробуем, пока не ответит.
            // play:false — переносим «на паузе», музыку за пользователя не включаем.
            var deadline = DateTime.UtcNow + DeviceWaitTimeout;
            while (true)
            {
                var local = FindLocalDevice(devices);
                if (local != null && await _api.TransferPlaybackAsync(local.Id, play: false))
                {
                    AppLog.Write("BackgroundPlayback", $"Playback transferred to '{local.Name}'");
                    return true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    AppLog.Write("BackgroundPlayback", "Local Spotify Connect device did not become available");
                    return false;
                }

                await Task.Delay(1500);
                devices = await _api.GetDevicesAsync();
                if (devices.Any(d => d.IsActive)) return true; // Пока ждали, воспроизведение уже где-то началось
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("BackgroundPlayback", ex);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Устройство этого ПК: тип Computer, имя совпадает с именем компьютера.
    /// Если имя не совпало, но компьютер в списке один — берём его.
    /// </summary>
    private static Device? FindLocalDevice(IReadOnlyList<Device> devices)
    {
        var computers = devices
            .Where(d => string.Equals(d.Type, "Computer", StringComparison.OrdinalIgnoreCase) && !d.IsRestricted)
            .ToList();

        return computers.FirstOrDefault(d => string.Equals(d.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
               ?? (computers.Count == 1 ? computers[0] : null);
    }
}
