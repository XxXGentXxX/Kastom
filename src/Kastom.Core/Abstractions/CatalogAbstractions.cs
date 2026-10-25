using Kastom.Core.Models;

namespace Kastom.Core.Abstractions;

public enum CatalogFetchStatus
{
    Ok,

    /// <summary>Каталог не змінився з минулої перевірки (ETag).</summary>
    NotModified,

    /// <summary>Мережа, сервер, файл — будь-яка помилка отримання.</summary>
    Failed,

    /// <summary>Підпис не збігся або вміст не пройшов перевірку. Такий каталог не використовується.</summary>
    InvalidSignature,
}

public sealed class CatalogFetchResult
{
    public required CatalogFetchStatus Status { get; init; }

    /// <summary>Перевірений каталог; лише для <see cref="CatalogFetchStatus.Ok"/>.</summary>
    public ModuleCatalog? Catalog { get; init; }

    /// <summary>Точні байти catalog.json (для кешу, який при запуску перевіряється повторно); лише для Ok.</summary>
    public byte[]? RawCatalog { get; init; }

    /// <summary>Вміст catalog.json.sig; лише для Ok.</summary>
    public string? Signature { get; init; }

    public string? Error { get; init; }
}

/// <summary>Звідки береться каталог: GitHub (https) або локальна папка розробника.</summary>
public interface ICatalogSource
{
    /// <summary>Чи можна встановлювати модулі з адрес file:// (лише локальний каталог розробника).</summary>
    bool AllowsFileUrls { get; }

    /// <summary>Отримати й перевірити каталог (підпис і вміст).</summary>
    Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>Останній перевірений каталог з кешем на диску.</summary>
public interface ICatalogService
{
    /// <summary>Останній перевірений каталог (з мережі або з кешу). Null — ще жодного.</summary>
    ModuleCatalog? Current { get; }

    DateTimeOffset? LastChecked { get; }

    bool AllowsFileUrls { get; }

    Task<CatalogFetchResult> RefreshAsync(CancellationToken cancellationToken);

    event EventHandler? CatalogChanged;
}
