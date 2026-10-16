namespace FluentSpotify.ViewModels;

public static class TextFormat
{
    /// <summary>"1 трек", "3 трека", "5 треков", "21 трек", "112 треков".</summary>
    public static string Tracks(int count)
    {
        int mod100 = count % 100;
        int mod10 = count % 10;
        string word = mod100 is >= 11 and <= 14 ? "треков"
            : mod10 == 1 ? "трек"
            : mod10 is >= 2 and <= 4 ? "трека"
            : "треков";
        return $"{count} {word}";
    }
}
