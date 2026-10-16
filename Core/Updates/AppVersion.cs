using System.Reflection;

namespace FluentSpotify.Core.Updates;

/// <summary>
/// Версия в стиле тегов релизов: v0.1.01-beta, 1.2, 1.2.3-rc.2. Числовые части сравниваются
/// как числа (01 == 1), пре-релиз (-beta) младше той же финальной версии,
/// метки пре-релиза сравниваются по частям: числа как числа, текст — без учёта регистра.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _preRelease;

    /// <summary>Исходная строка без префикса v и метаданных сборки (+sha).</summary>
    public string Text { get; }

    public bool IsPreRelease => _preRelease.Length > 0;

    private AppVersion(string text, int[] numbers, string[] preRelease)
    {
        Text = text;
        _numbers = numbers;
        _preRelease = preRelease;
    }

    public static bool TryParse(string? value, out AppVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string s = value.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string core = s;
        string pre = string.Empty;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            core = s[..dash];
            pre = s[(dash + 1)..];
        }

        string[] parts = core.Split('.');
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out numbers[i])) return false;
        }

        string[] preParts = pre.Length == 0
            ? []
            : pre.Split('.', StringSplitOptions.RemoveEmptyEntries);

        version = new AppVersion(s, numbers, preParts);
        return true;
    }

    /// <summary>Версия запущенного приложения из InformationalVersion сборки.</summary>
    public static AppVersion Current { get; } = TryParse(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        out var current) ? current : new AppVersion("0.0.0", [0, 0, 0], []);

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;

        int length = Math.Max(_numbers.Length, other._numbers.Length);
        for (int i = 0; i < length; i++)
        {
            int a = i < _numbers.Length ? _numbers[i] : 0;
            int b = i < other._numbers.Length ? other._numbers[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        // Финальная версия старше любого своего пре-релиза
        if (_preRelease.Length == 0 || other._preRelease.Length == 0)
        {
            return other._preRelease.Length.CompareTo(_preRelease.Length);
        }

        int preLength = Math.Min(_preRelease.Length, other._preRelease.Length);
        for (int i = 0; i < preLength; i++)
        {
            int c = ComparePreReleasePart(_preRelease[i], other._preRelease[i]);
            if (c != 0) return c;
        }
        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    /// <summary>"beta2" &lt; "beta10": текст и числа внутри части сравниваются по отдельности.</summary>
    private static int ComparePreReleasePart(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                int c = decimal.Parse(a[si..i]).CompareTo(decimal.Parse(b[sj..j]));
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => Text;
}
