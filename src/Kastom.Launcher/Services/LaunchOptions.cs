using System.Globalization;
using System.IO;
using System.Windows;

namespace Kastom.Launcher.Services;

/// <summary>Аргументи командного рядка лаунчера.</summary>
public sealed record LaunchOptions(string? CatalogDirectory, bool Minimized, string? RenderPagesDirectory, string[] RawArgs)
{
    /// <summary>Найменша тестова область: головне вікно має MinWidth 900 і MinHeight 600.</summary>
    private const double MinTestWidth = 900;
    private const double MinTestHeight = 600;

    /// <summary>
    /// --data-dir: пісочниця для перевірок. Усі файли лаунчера лежать у цій теці (local\, roaming\),
    /// автозапуск у реєстрі не змінюється, лаунчер не оновлює сам себе, а «один екземпляр» окремий від справжнього.
    /// </summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// --test-screen left,top,width,height (DIP): усі вікна лаунчера відкриваються в цій області й не забирають фокус.
    /// Працює лише разом з --data-dir, щоб перевірка не чіпала справжній стан. Неправильне значення дає область
    /// лівіше за всі монітори.
    /// </summary>
    public Rect? TestScreen { get; init; }

    /// <summary>Неправильні аргументи: лаунчер не стартує, а виходить з кодом 2.</summary>
    public string? Error { get; init; }

    public static LaunchOptions Parse(string[] args)
    {
        string? catalogDir = null;
        string? renderDir = null;
        string? dataDir = null;
        string? error = null;
        Rect? testScreen = null;
        var minimized = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog-dir":
                    catalogDir = TakeValue(args, ref i);
                    break;
                case "--render-pages":
                    renderDir = TakeValue(args, ref i);
                    break;
                case "--data-dir":
                    dataDir = TakeValue(args, ref i);
                    if (dataDir is null || !IsValidDirectory(dataDir)) error ??= "--data-dir: потрібна тека.";
                    break;
                case "--test-screen":
                    var value = TakeValue(args, ref i);
                    testScreen = value is not null && TryParseRect(value, out var rect) ? rect : OffScreen();
                    break;
                case "--minimized":
                    minimized = true;
                    break;
            }
        }

        if (testScreen is not null && dataDir is null) error ??= "--test-screen працює лише разом з --data-dir.";

        return new LaunchOptions(catalogDir, minimized, renderDir, args)
        {
            DataDirectory = dataDir,
            TestScreen = testScreen,
            Error = error,
        };
    }

    /// <summary>Значення параметра — наступний аргумент, якщо це не інший параметр.</summary>
    private static string? TakeValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return null;
        return args[++i];
    }

    private static bool IsValidDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryParseRect(string text, out Rect rect)
    {
        rect = Rect.Empty;
        var parts = text.Split(',');
        if (parts.Length != 4) return false;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                return false;
        }

        if (values[2] < MinTestWidth || values[3] < MinTestHeight) return false;
        rect = new Rect(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static Rect OffScreen() =>
        new(SystemParameters.VirtualScreenLeft - 3000, SystemParameters.VirtualScreenTop, 1600, 1000);
}
