namespace Kastom.Core;

/// <summary>
/// Адреси в інтернеті. Репозиторій створено 2026-10-25 як приватний: поки він приватний, ні каталог
/// (raw.githubusercontent.com), ні релізи для самооновлення без входу недоступні — лаунчер просто не знайде
/// оновлень. Для розробки каталог читається з локальної папки (аргумент лаунчера --catalog-dir).
/// </summary>
public static class KastomEndpoints
{
    public const string GitHubRepoUrl = "https://github.com/XxXGentXxX/Kastom";

    public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/XxXGentXxX/Kastom/main/catalog/catalog.json";
}
