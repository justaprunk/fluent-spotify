using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Localization;

/// <summary>Язык интерфейса: код ресурса (Strings/&lt;Code&gt;.json) и название на самом языке.</summary>
public sealed record AppLanguage(string Code, string NativeName);

/// <summary>
/// Строки интерфейса на выбранном языке. Переводы — JSON-файлы Strings/&lt;код&gt;.json,
/// встроенные в сборку: так одинаково работает и в распакованной (unpackaged) сборке,
/// и без resources.pri. Язык выбирается один раз при запуске (до создания окна);
/// смена языка в настройках применяется после перезапуска.
/// </summary>
public static class Localizer
{
    public const string FallbackLanguage = "en-US";

    /// <summary>Поддерживаемые языки в порядке списка в настройках.</summary>
    public static IReadOnlyList<AppLanguage> Languages { get; } =
    [
        new("en-US", "English"),
        new("fr-FR", "Français"),
        new("de-DE", "Deutsch"),
        new("nl-NL", "Nederlands"),
        new("ja-JP", "日本語"),
        new("ko-KR", "한국어"),
        new("pl-PL", "Polski"),
        new("pt-BR", "Português (Brasil)"),
        new("ru-RU", "Русский"),
        new("zh-Hans", "简体中文"),
        new("es-419", "Español (Latinoamérica)"),
        new("zh-Hant", "繁體中文"),
        new("tr-TR", "Türkçe"),
        new("uk-UA", "Українська"),
        new("es-ES", "Español (España)")
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> _strings = new();
    private static Dictionary<string, string> _fallback = new();

    /// <summary>Значение настройки «Язык» при запуске: с ним сравнивается новый выбор (нужен ли перезапуск).</summary>
    public static string? StartupSetting { get; set; }

    /// <summary>Действующий язык интерфейса (код из <see cref="Languages"/>).</summary>
    public static string CurrentLanguage { get; private set; } = FallbackLanguage;

    /// <summary>Культура действующего языка: даты, числа, регистр.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(FallbackLanguage);

    /// <summary>
    /// Выбирает язык: сохранённый в настройках или, если там пусто, язык интерфейса Windows
    /// (без подходящего перевода — английский).
    /// </summary>
    public static void Initialize(string? preferred)
    {
        CurrentLanguage = Resolve(preferred);
        _fallback = Load(FallbackLanguage);
        _strings = Load(CurrentLanguage);

        try
        {
            Culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        }
        catch (CultureNotFoundException)
        {
            Culture = CultureInfo.GetCultureInfo(FallbackLanguage);
        }

        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;

        try
        {
            // Встроенные строки WinUI (подсказки кнопок и т. п.) берутся из ресурсов Windows App SDK
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage;
        }
        catch (Exception ex)
        {
            AppLog.Write("Localizer", ex);
        }
    }

    /// <summary>Код языка для настройки: явный выбор или язык Windows.</summary>
    public static string Resolve(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred)
            && Languages.FirstOrDefault(l => l.Code.Equals(preferred, StringComparison.OrdinalIgnoreCase)) is { } chosen)
        {
            return chosen.Code;
        }
        return MatchSystemLanguage() ?? FallbackLanguage;
    }

    /// <summary>Подходящий перевод для языка интерфейса Windows или null.</summary>
    public static string? MatchSystemLanguage()
    {
        var candidates = new List<string>();
        try
        {
            candidates.AddRange(global::Windows.System.UserProfile.GlobalizationPreferences.Languages);
        }
        catch (Exception ex)
        {
            AppLog.Write("Localizer", ex);
        }
        candidates.Add(CultureInfo.InstalledUICulture.Name);

        foreach (string tag in candidates)
        {
            if (Match(tag) is { } code) return code;
        }
        return null;
    }

    /// <summary>BCP-47 тег Windows → код перевода: zh-CN → zh-Hans, es-MX → es-419, pt-PT → pt-BR и т. д.</summary>
    public static string? Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        if (Languages.FirstOrDefault(l => l.Code.Equals(tag, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact.Code;
        }

        string[] parts = tag.Split('-');
        string language = parts[0].ToLowerInvariant();
        string rest = tag.Length > language.Length ? tag[(language.Length + 1)..].ToLowerInvariant() : string.Empty;

        switch (language)
        {
            case "zh":
                return rest.Contains("hant") || rest is "tw" or "hk" or "mo" || rest.EndsWith("-tw") || rest.EndsWith("-hk") || rest.EndsWith("-mo")
                    ? "zh-Hant"
                    : "zh-Hans";
            case "es":
                return rest.Length == 0 || rest.StartsWith("es") ? "es-ES" : "es-419";
            case "pt":
                return "pt-BR";
        }

        return Languages.FirstOrDefault(l => l.Code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))?.Code;
    }

    /// <summary>Строка по ключу на текущем языке (нет перевода — английская, нет и её — сам ключ).</summary>
    public static string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : key;

    /// <summary>Строка с подстановкой аргументов в культуре текущего языка.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    /// <summary>Строка на заданном языке — например, подсказка о перезапуске на только что выбранном языке.</summary>
    public static string GetIn(string language, string key) =>
        Load(language).TryGetValue(key, out var value) ? value : Get(key);

    /// <summary>
    /// Количество с правильной формой слова: ключи key_one / key_few / key_many / key_other
    /// по правилам CLDR для текущего языка, {0} — число.
    /// </summary>
    public static string Plural(string key, long count)
    {
        string category = PluralRules.Category(CurrentLanguage, count);
        string format = _strings.TryGetValue($"{key}_{category}", out var value) ? value
            : _strings.TryGetValue($"{key}_other", out var other) ? other
            : _fallback.TryGetValue($"{key}_{(count == 1 ? "one" : "other")}", out var fallback) ? fallback
            : key;
        return string.Format(Culture, format, count);
    }

    /// <summary>Дата без дня недели в принятом для языка виде (17 октября 2026, October 17, 2026, 2026年10月17日).</summary>
    public static string FormatDate(DateTimeOffset date) => date.ToString(Get("Format_Date"), Culture);

    public static string FormatTime(DateTimeOffset time) => time.ToString(Culture.DateTimeFormat.ShortTimePattern, Culture);

    private static Dictionary<string, string> Load(string language)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(language, out var cached)) return cached;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? resource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith($".Strings.{language}.json", StringComparison.OrdinalIgnoreCase));
                if (resource != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resource)!;
                    result = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? result;
                }
                else
                {
                    AppLog.Write("Localizer", $"Нет перевода {language}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Localizer", ex);
            }

            Cache[language] = result;
            return result;
        }
    }
}

/// <summary>Правила множественного числа CLDR для целых чисел (только поддерживаемые языки).</summary>
public static class PluralRules
{
    public static string Category(string language, long n)
    {
        n = Math.Abs(n);
        long mod10 = n % 10;
        long mod100 = n % 100;

        switch (language[..2])
        {
            case "ja" or "ko" or "zh":
                return "other";

            case "ru" or "uk":
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
                return "many";

            case "pl":
                if (n == 1) return "one";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
                return "many";

            // fr: 0 и 1 — единственное число; pt-BR: i = 0..1
            case "fr" or "pt":
                return n is 0 or 1 ? "one" : "other";

            default: // en, de, nl, es, tr
                return n == 1 ? "one" : "other";
        }
    }
}
