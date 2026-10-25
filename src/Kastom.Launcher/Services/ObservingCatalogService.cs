using Kastom.Core.Abstractions;
using Kastom.Core.Models;

namespace Kastom.Launcher.Services;

/// <summary>
/// Обгортка над ICatalogService, що запам'ятовує результат останнього оновлення:
/// сторінка «Модулі» показує з нього помилку каталогу (в самому ICatalogService її немає).
/// </summary>
public sealed class ObservingCatalogService(ICatalogService inner) : ICatalogService
{
    public CatalogFetchResult? LastResult { get; private set; }

    public ModuleCatalog? Current => inner.Current;

    public DateTimeOffset? LastChecked => inner.LastChecked;

    public bool AllowsFileUrls => inner.AllowsFileUrls;

    public event EventHandler? CatalogChanged
    {
        add => inner.CatalogChanged += value;
        remove => inner.CatalogChanged -= value;
    }

    /// <summary>Оновлення завершилося (успішно чи ні); подія приходить у потоці, що робив оновлення.</summary>
    public event EventHandler? RefreshCompleted;

    public async Task<CatalogFetchResult> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await inner.RefreshAsync(cancellationToken);
            LastResult = result;
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastResult = new CatalogFetchResult { Status = CatalogFetchStatus.Failed, Error = exception.Message };
            throw;
        }
        finally
        {
            RefreshCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
