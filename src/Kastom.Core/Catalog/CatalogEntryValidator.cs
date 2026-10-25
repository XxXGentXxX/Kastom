using Kastom.Core.Models;

namespace Kastom.Core.Catalog;

/// <summary>Перевірка запису каталогу: її роблять і перевіряльник каталогу, і встановлювач, і CatalogTool.</summary>
public static class CatalogEntryValidator
{
    /// <summary>Найбільший розмір пакета: 512 МБ.</summary>
    public const long MaxPackageSize = 512L * 1024 * 1024;

    /// <summary>Повертає опис проблеми українською або null, якщо запис правильний.</summary>
    public static string? Validate(CatalogEntry entry, bool allowFileUrls)
    {
        if (!ModuleManifest.IsValidId(entry.Id))
            return $"Неправильний id модуля: '{entry.Id}'.";

        var who = $"Модуль '{entry.Id}'";
        if (string.IsNullOrWhiteSpace(entry.Name))
            return $"{who}: порожня назва (name).";
        if (!IsSafeVersion(entry.Version) || !ModuleVersion.TryParse(entry.Version, out _))
            return $"{who}: неправильна версія (version): '{entry.Version}'.";
        if (!ModuleVersion.TryParse(entry.MinLauncherVersion, out _))
            return $"{who}: неправильна мінімальна версія лаунчера (minLauncherVersion): '{entry.MinLauncherVersion}'.";
        if (!IsValidSha256(entry.Sha256))
            return $"{who}: SHA-256 (sha256) має бути 64 шістнадцятковими символами в нижньому регістрі.";
        if (entry.Size is < 0 or > MaxPackageSize)
            return $"{who}: розмір пакета (size) поза межами від 0 до {MaxPackageSize} байтів.";
        if (entry.ContractVersion <= 0)
            return $"{who}: версія контракту (contractVersion) має бути додатною.";

        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url))
            return $"{who}: адреса пакета (url) не є повною адресою.";
        var allowed = url.Scheme == Uri.UriSchemeHttps || (allowFileUrls && url.Scheme == Uri.UriSchemeFile);
        if (!allowed)
            return $"{who}: адреса пакета (url) має починатися з https://{(allowFileUrls ? " або file://" : "")}.";

        return null;
    }

    /// <summary>Перевірка опису релізу лаунчера: версія, SHA-256 і ім'я файлу пакета Velopack.</summary>
    public static string? ValidateLauncher(LauncherRelease launcher)
    {
        if (!IsSafeVersion(launcher.Version) || !ModuleVersion.TryParse(launcher.Version, out _))
            return $"Реліз лаунчера: неправильна версія '{launcher.Version}'.";
        if (!IsValidSha256(launcher.Sha256))
            return "Реліз лаунчера: SHA-256 має бути 64 шістнадцятковими символами в нижньому регістрі.";
        var name = launcher.FileName;
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return $"Реліз лаунчера: ім'я файлу має бути простим іменем пакета .nupkg, без шляху: '{name}'.";
        return null;
    }

    /// <summary>
    /// Версія стає назвою папки, тож крім розбору SemVer допускаються лише цифри, латиниця, крапки, дефіси й плюс
    /// (жодних роздільників шляху й пробілів).
    /// </summary>
    public static bool IsSafeVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 64) return false;
        foreach (var c in version)
        {
            var ok = c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '.' or '-' or '+';
            if (!ok) return false;
        }

        return version[^1] != '.';
    }

    public static bool IsValidSha256(string? text)
    {
        if (text is not { Length: 64 }) return false;
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        }

        return true;
    }
}
