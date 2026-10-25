namespace Kastom.Core;

/// <summary>
/// Адреси в інтернеті. Репозиторій публічний (з 2026-10-25): каталог читається з raw.githubusercontent.com,
/// файли модулів і оновлення лаунчера — з GitHub Releases, усе без входу. Для розробки каталог читається з
/// локальної папки (аргумент лаунчера --catalog-dir).
/// </summary>
public static class KastomEndpoints
{
    public const string GitHubRepoUrl = "https://github.com/XxXGentXxX/Kastom";

    public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/XxXGentXxX/Kastom/main/catalog/catalog.json";
}
