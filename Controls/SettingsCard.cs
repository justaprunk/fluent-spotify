using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentSpotify.Controls;

/// <summary>
/// Карточка параметра в стиле «Параметров» Windows 11 (как SettingsCard из Community Toolkit):
/// значок, заголовок с описанием и элемент управления справа. Если места для текста мало
/// (узкое окно или длинный перевод), элемент управления переносится под текст.
/// Шаблон — неявный стиль в App.xaml.
/// </summary>
public sealed partial class SettingsCard : ContentControl
{
    // Уже этого текст рядом с элементом управления читается плохо — переносим элемент вниз
    private const double MinTextWidth = 240;

    private FrameworkElement? _textPanel;
    private FrameworkElement? _contentPresenter;
    private FrameworkElement? _description;
    private FrameworkElement? _icon;
    private FrameworkElement? _image;
    private bool _isWrapped;

    public SettingsCard()
    {
        SizeChanged += (s, e) => UpdateWrap(e.NewSize.Width);
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Строка или разметка под заголовком; пусто — строка не показывается.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(object), typeof(SettingsCard),
        new PropertyMetadata(null, (d, e) => ((SettingsCard)d).UpdateParts()));

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Значок Segoe Fluent Icons.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard),
        new PropertyMetadata(string.Empty, (d, e) => ((SettingsCard)d).UpdateParts()));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Картинка вместо значка (например, логотип приложения).</summary>
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(ImageSource), typeof(SettingsCard),
        new PropertyMetadata(null, (d, e) => ((SettingsCard)d).UpdateParts()));

    public ImageSource? Image
    {
        get => (ImageSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textPanel = GetTemplateChild("PART_Text") as FrameworkElement;
        _contentPresenter = GetTemplateChild("PART_Content") as FrameworkElement;
        _description = GetTemplateChild("PART_Description") as FrameworkElement;
        _icon = GetTemplateChild("PART_Icon") as FrameworkElement;
        _image = GetTemplateChild("PART_Image") as FrameworkElement;
        _isWrapped = false;
        UpdateParts();
        UpdateWrap(ActualWidth);
    }

    private void UpdateParts()
    {
        bool hasDescription = Description is not (null or "");
        if (_description != null) _description.Visibility = hasDescription ? Visibility.Visible : Visibility.Collapsed;
        if (_image != null) _image.Visibility = Image != null ? Visibility.Visible : Visibility.Collapsed;
        if (_icon != null) _icon.Visibility = Image == null && !string.IsNullOrEmpty(Glyph) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWrap(double width)
    {
        if (_contentPresenter == null || _textPanel == null || width <= 0) return;

        // Ширина элемента управления не зависит от того, где он стоит: меряем без ограничений
        _contentPresenter.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = _contentPresenter.DesiredSize.Width;
        if (contentWidth <= 0)
        {
            SetWrapped(false);
            return;
        }

        // Рамка, отступы, колонка значка и промежутки сетки
        double chrome = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right + 40 + 16;
        SetWrapped(width - chrome - contentWidth < MinTextWidth);
    }

    private void SetWrapped(bool wrapped)
    {
        if (_contentPresenter == null || wrapped == _isWrapped) return;
        _isWrapped = wrapped;

        Grid.SetRow(_contentPresenter, wrapped ? 1 : 0);
        Grid.SetColumn(_contentPresenter, wrapped ? 1 : 2);
        _contentPresenter.HorizontalAlignment = wrapped ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _contentPresenter.Margin = wrapped ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }
}
