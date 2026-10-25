using System.Windows;
using System.Windows.Controls;

namespace Kastom.Launcher.Settings;

/// <summary>Спільні дрібниці для елементів E4b: пензлі теми (світла й темна читаються однаково), картки, підписи.</summary>
internal static class UiTheme
{
    private static Style? _switchStyle;

    public static Style SwitchStyle
    {
        get
        {
            if (_switchStyle is null)
            {
                var uri = new Uri($"/{typeof(UiTheme).Assembly.GetName().Name};component/Settings/SwitchStyle.xaml", UriKind.Relative);
                _switchStyle = (Style)new ResourceDictionary { Source = uri }["KastomSwitchStyle"];
            }

            return _switchStyle;
        }
    }

    public static TextBlock Label(string text, double size = 14, bool secondary = false, bool semibold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return block;
    }

    /// <summary>Рядок помилки під елементом; прихований, доки не викличуть <see cref="ShowError"/>.</summary>
    public static TextBlock ErrorLabel()
    {
        var block = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        return block;
    }

    public static void ShowError(TextBlock label, string? message)
    {
        label.Text = message ?? "";
        label.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Border Card(UIElement child, Thickness margin)
    {
        var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = margin, Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return border;
    }

    /// <summary>Панель-підкладка вікна: напівпрозорий фон теми + тонка рамка. Вміст кладуть поверх (не успадковує прозорість).</summary>
    public static Grid WindowSurface(UIElement content, double cornerRadius = 8)
    {
        var background = new Border { CornerRadius = new CornerRadius(cornerRadius), Opacity = 0.94 };
        background.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        var stroke = new Border { CornerRadius = new CornerRadius(cornerRadius), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        stroke.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var grid = new Grid();
        grid.Children.Add(background);
        grid.Children.Add(content);
        grid.Children.Add(stroke);
        return grid;
    }

    public static string FormatError(Exception exception) => exception.Message;
}
