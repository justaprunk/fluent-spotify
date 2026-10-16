using FluentSpotify.Localization;

namespace FluentSpotify.ViewModels;

public static class TextFormat
{
    /// <summary>"1 track", "5 треков", "3 utwory", "12 曲" — по правилам множественного числа языка.</summary>
    public static string Tracks(int count) => Localizer.Plural("Tracks", count);
}
