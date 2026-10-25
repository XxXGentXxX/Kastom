namespace Kastom.Core.Catalog;

/// <summary>
/// Правила значка модуля: ті самі в CatalogTool, перевіряльнику каталогу, установнику й менеджері модулів,
/// щоб значок, який видавець прийняв, лаунчер теж прийняв, а підсунутий — ні.
/// </summary>
public static class ModuleIcons
{
    /// <summary>Найбільший розмір PNG значка: 32 КБ.</summary>
    public const int MaxSize = 32 * 1024;

    // base64 кодує кожні 3 байти чотирма символами, з доповненням до кратного трьом.
    private const int MaxBase64Length = (MaxSize + 2) / 3 * 4;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Шлях до значка в пакеті: відносний, зі слешами "/", без "..", диска й початкового слеша.
    /// Повертає опис проблеми українською або null, якщо шлях безпечний.
    /// </summary>
    public static string? ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "шлях до значка порожній.";
        if (path.Contains('\\')) return $"у шляху до значка '{path}' має бути слеш '/', а не '\\'.";
        if (path[0] == '/' || Path.IsPathRooted(path) || path.Contains(':'))
            return $"шлях до значка '{path}' має бути відносним, без кореня й диска.";
        if (path.Contains(".."))
            return $"шлях до значка '{path}' не може містити '..'.";
        if (path[^1] == '/' || path.Contains("//") || path.Split('/').Any(segment => segment == "."))
            return $"шлях до значка '{path}' має вказувати на файл.";
        return null;
    }

    /// <summary>Вміст значка: непорожній, до <see cref="MaxSize"/> байтів, з підписом PNG. Повертає опис проблеми або null.</summary>
    public static string? ValidateData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "файл значка порожній.";
        if (data.Length > MaxSize) return $"значок завеликий: {data.Length} байтів, найбільше {MaxSize}.";
        if (!data.StartsWith(PngSignature)) return "значок має бути файлом PNG (немає підпису PNG на початку).";
        return null;
    }

    /// <summary>Розкодовує значок з каталогу (base64). Неправильний — false і опис проблеми; виняток не кидається.</summary>
    public static bool TryDecode(string? base64, out byte[]? png, out string? problem)
    {
        png = null;
        problem = null;
        if (base64 is null) return true;
        if (base64.Length > MaxBase64Length)
        {
            problem = $"значок завеликий: понад {MaxSize} байтів.";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            problem = "значок не є правильним base64.";
            return false;
        }

        problem = ValidateData(bytes);
        if (problem is not null) return false;
        png = bytes;
        return true;
    }

    /// <summary>
    /// Читає значок із папки встановленої версії. Null — значка немає: шлях небезпечний, файлу нема або він не проходить
    /// перевірки; причина в <paramref name="problem"/>. Не кидає винятків читання: значок не має заважати завантаженню модуля.
    /// </summary>
    public static byte[]? ReadFromDirectory(string directory, string iconPath, out string? problem)
    {
        problem = ValidatePath(iconPath);
        if (problem is not null) return null;

        try
        {
            var root = Path.GetFullPath(directory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, iconPath));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                problem = $"шлях до значка '{iconPath}' виходить за межі папки модуля.";
                return null;
            }

            var file = new FileInfo(full);
            if (!file.Exists)
            {
                problem = $"файлу значка '{iconPath}' немає в пакеті.";
                return null;
            }

            if (file.Length > MaxSize)
            {
                problem = $"значок завеликий: {file.Length} байтів, найбільше {MaxSize}.";
                return null;
            }

            var bytes = File.ReadAllBytes(full);
            problem = ValidateData(bytes);
            return problem is null ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = $"значок не прочитано: {ex.Message}";
            return null;
        }
    }
}
