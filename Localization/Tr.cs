using Microsoft.UI.Xaml.Markup;

namespace FluentSpotify.Localization;

/// <summary>
/// Строка интерфейса в XAML: Text="{l:Tr Key=Settings_Title}".
/// Значение подставляется при загрузке разметки на языке, выбранном при запуске.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Tr : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Localizer.Get(Key);
}
