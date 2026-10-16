using System.Text.Json.Serialization;

namespace FluentSpotify.Shared.Windows;

// Формат ~/.config/wshm/theme.json — должен совпадать с Wshm.Common.Ipc в wshm-ui-common.

public record ShellWallpaperConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "Image"; // "Image", "Slideshow", "Web", "Video"

    [JsonPropertyName("path")]
    public string Path { get; init; } = ""; // Локальный путь к файлу/папке или http(s) URL

    [JsonPropertyName("slideshowIntervalSec")]
    public int SlideshowIntervalSec { get; init; } = 300;

    [JsonPropertyName("playbackPaused")]
    public bool PlaybackPaused { get; init; } = false;
}

public record ShellThemeConfig
{
    [JsonPropertyName("backdrop")]
    public string Backdrop { get; init; } = "MicaAlt";

    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "Dark";

    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get; init; } = 16.0;

    [JsonPropertyName("taskbarHeight")]
    public int TaskbarHeight { get; init; } = 58;

    /// <summary>
    /// Синхронизировать светлую/тёмную тему Windows (реестр HKCU) с темой шелла.
    /// </summary>
    [JsonPropertyName("syncSystemTheme")]
    public bool SyncSystemTheme { get; init; } = true;

    [JsonPropertyName("wallpaper")]
    public ShellWallpaperConfig Wallpaper { get; init; } = new();
}
