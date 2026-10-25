namespace Kastom.Core;

/// <summary>
/// Адреси в інтернеті. Репозиторій на GitHub ще не створено, тож власник — заглушка;
/// для розробки каталог читається з локальної папки (аргумент лаунчера --catalog-dir).
/// </summary>
public static class KastomEndpoints
{
    public const string GitHubRepoUrl = "https://github.com/OWNER/Kastom";

    public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/OWNER/Kastom/main/catalog/catalog.json";
}
