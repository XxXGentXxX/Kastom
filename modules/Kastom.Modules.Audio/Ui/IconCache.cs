using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Значки програм і пристроїв за шляхом зі звукового сеансу чи реєстру Windows. Викликається з потоку інтерфейсу.
/// Невдачі теж кешуються, щоб програма без значка не опитувала систему при кожному оновленні.
/// </summary>
internal static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Значок за шляхом: "C:\...\app.exe" або "файл,-id" ("%windir%\system32\mmres.dll,-3010"). Немає значка — null.</summary>
    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        var icon = Load(path);
        lock (Cache) Cache[path] = icon;
        return icon;
    }

    private static ImageSource? Load(string path)
    {
        try
        {
            var text = path.Trim().Trim('"');

            // "@{...}" і ms-resource — рядки пакетних програм, значка в них не дістати.
            if (text.StartsWith('@') || text.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)) return null;

            var index = 0;
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                // Від'ємне число — ідентифікатор ресурсу значка, ExtractIconEx розуміє його як є.
                index = parsed;
                text = text[..comma].Trim().Trim('"');
            }

            text = Environment.ExpandEnvironmentVariables(text);
            var large = new IntPtr[1];
            if (ExtractIconEx(text, index, large, null, 1) < 1 || large[0] == IntPtr.Zero) return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(large[0]);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string file, int iconIndex, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
