using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kastom.Launcher.Shell;

/// <summary>
/// Значок модуля заданого розміру: PNG з <c>ModuleInfo.Icon</c> або, якщо його немає чи він не декодується,
/// заокруглений квадрат кольору акценту з першою літерою назви.
/// </summary>
public sealed class ModuleIconView : ContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(object), typeof(ModuleIconView), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ModuleNameProperty = DependencyProperty.Register(
        nameof(ModuleName), typeof(string), typeof(ModuleIconView), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(ModuleIconView), new PropertyMetadata(32d, OnChanged));

    // Один і той самий масив ModuleInfo.Icon декодуємо раз, а не при кожній перебудові списку.
    private static readonly ConditionalWeakTable<byte[], ImageSource> Decoded = new();

    public ModuleIconView()
    {
        IsTabStop = false;
        Focusable = false;
        Rebuild();
    }

    /// <summary>Байти PNG (<c>ModuleInfo.Icon</c>) або null. Тип object, бо XAML вважає властивість типу byte[] масивом і не дає прив'язати її в шаблоні.</summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string ModuleName
    {
        get => (string)GetValue(ModuleNameProperty);
        set => SetValue(ModuleNameProperty, value);
    }

    /// <summary>Сторона квадрата в DIP.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Безпечне декодування: будь-яка помилка (пошкоджений PNG, не картинка) дає null, а не падіння сторінки.</summary>
    public static ImageSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        if (Decoded.TryGetValue(png, out var cached)) return cached;

        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;

            // Значок показується не більше 32 DIP (64 px при 200 %): великий PNG не має розгортатися в пам'яті повністю.
            image.DecodePixelWidth = 64;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Decoded.AddOrUpdate(png, image);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Перша літера назви великою; для порожньої назви знак питання.</summary>
    public static string FirstLetter(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "?";
        var length = char.IsHighSurrogate(trimmed[0]) && trimmed.Length > 1 ? 2 : 1;
        return trimmed[..length].ToUpperInvariant();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ModuleIconView)d).Rebuild();

    private void Rebuild()
    {
        var size = Size;
        Width = size;
        Height = size;

        if (Decode(Icon as byte[]) is { } image)
        {
            var picture = new Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            Content = picture;
            return;
        }

        var letter = new TextBlock
        {
            Text = FirstLetter(ModuleName),
            FontSize = size * 0.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), Child = letter };
        tile.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        Content = tile;
    }
}
