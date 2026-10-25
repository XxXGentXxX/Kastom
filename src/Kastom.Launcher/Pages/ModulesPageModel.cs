using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;

namespace Kastom.Launcher.Pages;

/// <summary>Стан сторінки «Модулі»: шапка з перевіркою оновлень, банер перезапуску, картки модулів.</summary>
public sealed class ModulesPageModel : ObservableObject, IDisposable
{
    private readonly LauncherServices _services;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Dictionary<string, ModuleCardModel> _cards = new();
    private int _runningOperations;
    private int _refreshQueued;
    private bool _checking;
    private bool _disposed;
    private string? _checkError;
    private string _lastCheckText = "";
    private string _catalogStatusText = "";
    private bool _catalogStatusIsError;
    private bool _showRestartBanner;
    private string? _loadErrorText;

    public ModulesPageModel(LauncherServices services, Func<string, bool> confirm)
    {
        _services = services;
        Confirm = confirm;

        services.Modules.Changed += OnExternalChange;
        services.Updates.StateChanged += OnExternalChange;
        services.Catalog.RefreshCompleted += OnExternalChange;
        services.Catalog.CatalogChanged += OnExternalChange;

        Refresh();
    }

    public ObservableCollection<ModuleCardModel> Cards { get; } = [];

    public Func<string, bool> Confirm { get; }

    public string LastCheckText
    {
        get => _lastCheckText;
        private set => SetField(ref _lastCheckText, value);
    }

    public string CatalogStatusText
    {
        get => _catalogStatusText;
        private set => SetField(ref _catalogStatusText, value);
    }

    public bool CatalogStatusIsError
    {
        get => _catalogStatusIsError;
        private set => SetField(ref _catalogStatusIsError, value);
    }

    public bool ShowRestartBanner
    {
        get => _showRestartBanner;
        private set => SetField(ref _showRestartBanner, value);
    }

    public string? LoadErrorText
    {
        get => _loadErrorText;
        private set
        {
            if (!SetField(ref _loadErrorText, value)) return;
            Raise(nameof(HasLoadError));
            Raise(nameof(ShowEmptyHint));
        }
    }

    public bool HasLoadError => _loadErrorText is not null;

    public bool ShowEmptyHint => Cards.Count == 0 && !HasLoadError;

    public string CheckButtonText => _checking || IsUpdateServiceChecking() ? Strings.Checking : Strings.CheckUpdates;

    public bool CanCheck => !_checking && !IsUpdateServiceChecking();

    /// <summary>Поки виконується будь-яка операція, кнопки всіх карток вимкнені.</summary>
    public bool AnyOperationRunning => _runningOperations > 0;

    internal Kastom.Sdk.Services.IModuleLogger Log => _services.Log;

    /// <summary>Токен скасування для користувацьких операцій: спрацьовує при виході з лаунчера.</summary>
    internal CancellationToken OperationToken => _services.OperationsCts.Token;

    public async Task CheckNowAsync()
    {
        if (_checking) return;
        _checking = true;
        _checkError = null;
        RaiseCheckState();
        try
        {
            await _services.Updates.CheckNowAsync();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося перевірити оновлення", exception);
            _checkError = exception.Message;
        }
        finally
        {
            _checking = false;
            Refresh();
        }
    }

    internal void OperationStarted()
    {
        _runningOperations++;
        RaiseButtonsState();
    }

    internal void OperationFinished()
    {
        _runningOperations--;
        RaiseButtonsState();
    }

    internal void Refresh()
    {
        if (_disposed) return;

        IReadOnlyList<ModuleInfo> infos;
        try
        {
            infos = _services.Modules.Modules;
            LoadErrorText = null;
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося отримати список модулів", exception);
            LoadErrorText = string.Format(Strings.ModulesLoadError, exception.Message);
            infos = [];
        }

        var desired = new List<ModuleCardModel>(infos.Count);
        foreach (var info in infos)
        {
            if (!_cards.TryGetValue(info.Id, out var card))
            {
                card = new ModuleCardModel(this, _services.Modules);
                _cards[info.Id] = card;
            }

            card.Update(info);
            desired.Add(card);
        }

        foreach (var id in _cards.Keys.Where(id => infos.All(info => info.Id != id)).ToList()) _cards.Remove(id);
        foreach (var stale in Cards.Where(card => !desired.Contains(card)).ToList()) Cards.Remove(stale);
        for (var i = 0; i < desired.Count; i++)
        {
            var index = Cards.IndexOf(desired[i]);
            if (index < 0) Cards.Insert(i, desired[i]);
            else if (index != i) Cards.Move(index, i);
        }

        ShowRestartBanner = infos.Any(info => info.RestartRequired);
        Raise(nameof(ShowEmptyHint));
        RefreshHeader();
        RaiseCheckState();
    }

    private void RefreshHeader()
    {
        DateTimeOffset? lastCheck = null;
        try
        {
            lastCheck = _services.Updates.LastCheck ?? _services.Catalog.LastChecked;
        }
        catch (Exception)
        {
            // Служба недоступна: покажемо «Ще не перевіряли».
        }

        LastCheckText = lastCheck is { } time
            ? string.Format(Strings.LastCheckFormat, time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Strings.LastCheckNever;

        var result = _services.Catalog.LastResult;
        string status;
        var isError = false;
        if (_checkError is not null)
        {
            status = string.Format(Strings.CatalogFailed, _checkError);
            isError = true;
        }
        else if (result is { Status: CatalogFetchStatus.Failed })
        {
            status = string.Format(Strings.CatalogFailed, result.Error ?? "");
            isError = true;
        }
        else if (result is { Status: CatalogFetchStatus.InvalidSignature })
        {
            status = Strings.CatalogInvalidSignature;
            isError = true;
        }
        else if (SafeCatalogCount() is { } count)
        {
            status = string.Format(Strings.CatalogCount, count);
        }
        else
        {
            status = Strings.CatalogNotLoaded;
        }

        CatalogStatusText = status;
        CatalogStatusIsError = isError;
    }

    private int? SafeCatalogCount()
    {
        try
        {
            return _services.Catalog.Current?.Modules.Count;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsUpdateServiceChecking()
    {
        try
        {
            return _services.Updates.IsChecking;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RaiseCheckState()
    {
        Raise(nameof(CheckButtonText));
        Raise(nameof(CanCheck));
    }

    private void RaiseButtonsState()
    {
        foreach (var card in _cards.Values) card.RaiseButtonsEnabled();
    }

    /// <summary>Події служб приходять у довільних потоках: збираємо їх в одне оновлення в потоці інтерфейсу.</summary>
    private void OnExternalChange(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _services.Modules.Changed -= OnExternalChange;
        _services.Updates.StateChanged -= OnExternalChange;
        _services.Catalog.RefreshCompleted -= OnExternalChange;
        _services.Catalog.CatalogChanged -= OnExternalChange;
    }
}

public enum ChipKind
{
    Neutral,
    Good,
    Accent,
    Warning,
    Error,
}

/// <summary>Картка одного модуля.</summary>
public sealed class ModuleCardModel : ObservableObject
{
    private static readonly Brush NeutralBrush = Frozen(0x30, 0x80, 0x80, 0x80);
    private static readonly Brush GoodBrush = Frozen(0x45, 0x2E, 0xA0, 0x43);
    private static readonly Brush AccentBrush = Frozen(0x45, 0x0F, 0x6C, 0xBD);
    private static readonly Brush WarningBrush = Frozen(0x50, 0xF2, 0xA9, 0x00);
    private static readonly Brush ErrorBrush = Frozen(0x50, 0xD1, 0x34, 0x38);

    private readonly ModulesPageModel _owner;
    private readonly IModuleManager _modules;
    private string _id = "";
    private string _name = "";
    private string _description = "";
    private string _versionsText = "";
    private string _statusText = "";
    private Brush _chipBackground = NeutralBrush;
    private bool _canInstall;
    private string _installText = Strings.Install;
    private bool _canUpdate;
    private bool _canToggle;
    private string _toggleText = "";
    private bool _canRollback;
    private string _rollbackText = "";
    private bool _canUninstall;
    private bool _isEnabledModule;
    private bool _isBusy;
    private bool _progressIsIndeterminate = true;
    private double _progressValue;
    private string? _message;
    private bool _messageIsError;

    internal ModuleCardModel(ModulesPageModel owner, IModuleManager modules)
    {
        _owner = owner;
        _modules = modules;
    }

    public string Id { get => _id; private set => SetField(ref _id, value); }

    public string Name { get => _name; private set => SetField(ref _name, value); }

    public string Description { get => _description; private set => SetField(ref _description, value); }

    public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

    public string VersionsText { get => _versionsText; private set => SetField(ref _versionsText, value); }

    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    public Brush ChipBackground { get => _chipBackground; private set => SetField(ref _chipBackground, value); }

    public bool CanInstall { get => _canInstall; private set => SetField(ref _canInstall, value); }

    public string InstallText { get => _installText; private set => SetField(ref _installText, value); }

    public bool CanUpdate { get => _canUpdate; private set => SetField(ref _canUpdate, value); }

    public bool CanToggle { get => _canToggle; private set => SetField(ref _canToggle, value); }

    public string ToggleText { get => _toggleText; private set => SetField(ref _toggleText, value); }

    public bool CanRollback { get => _canRollback; private set => SetField(ref _canRollback, value); }

    public string RollbackText { get => _rollbackText; private set => SetField(ref _rollbackText, value); }

    public bool CanUninstall { get => _canUninstall; private set => SetField(ref _canUninstall, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) RaiseButtonsEnabled();
        }
    }

    public bool ProgressIsIndeterminate { get => _progressIsIndeterminate; private set => SetField(ref _progressIsIndeterminate, value); }

    /// <summary>Частка від 0 до 1.</summary>
    public double ProgressValue { get => _progressValue; private set => SetField(ref _progressValue, value); }

    public string? Message { get => _message; private set { if (SetField(ref _message, value)) Raise(nameof(HasMessage)); } }

    public bool HasMessage => _message is not null;

    public bool MessageIsError { get => _messageIsError; private set => SetField(ref _messageIsError, value); }

    public bool ButtonsEnabled => !_owner.AnyOperationRunning;

    internal void RaiseButtonsEnabled() => Raise(nameof(ButtonsEnabled));

    internal void Update(ModuleInfo info)
    {
        Id = info.Id;
        Name = info.Name;
        Description = info.Description;
        Raise(nameof(HasDescription));
        _isEnabledModule = info.IsEnabled;

        var versions = new List<string>(2);
        if (info.InstalledVersion is { } installed) versions.Add(string.Format(Strings.VersionsInstalled, installed));
        if (info.AvailableVersion is { } available) versions.Add(string.Format(Strings.VersionsAvailable, available));
        var versionsText = string.Join(Strings.VersionsSeparator, versions);
        if (info.IsInstalled && !info.IsCompatible && info.AvailableVersion is not null)
            versionsText += Strings.VersionsIncompatibleNote;
        VersionsText = versionsText;

        (StatusText, var kind) = Status(info);
        ChipBackground = kind switch
        {
            ChipKind.Good => GoodBrush,
            ChipKind.Accent => AccentBrush,
            ChipKind.Warning => WarningBrush,
            ChipKind.Error => ErrorBrush,
            _ => NeutralBrush,
        };

        // Модуль без файлів (лаунчер перевстановлювали) не вмикається, а перевстановлюється.
        var missing = info.IsMissing || info.IsRepairing;
        CanInstall = info.AvailableVersion is not null && info.IsCompatible
                     && (!info.IsInstalled || (info.IsMissing && !info.IsRepairing));
        InstallText = info.IsMissing ? Strings.Reinstall : Strings.Install;
        CanUpdate = info.IsInstalled && info.HasUpdate && !missing;
        CanToggle = info.IsInstalled && !missing;
        ToggleText = info.IsEnabled ? Strings.Disable : Strings.Enable;
        CanRollback = info.IsInstalled && info.PreviousVersion is not null && !missing;
        RollbackText = info.PreviousVersion is { } previous ? string.Format(Strings.Rollback, previous) : "";
        CanUninstall = info.IsInstalled;
    }

    private static (string Text, ChipKind Kind) Status(ModuleInfo info)
    {
        if (info.IsRepairing) return (Strings.StatusRepairing, ChipKind.Accent);
        if (info.IsMissing)
        {
            return info.Error is { } reason
                ? (string.Format(Strings.StatusMissingWithReason, reason), ChipKind.Error)
                : (Strings.StatusMissing, ChipKind.Error);
        }

        if (info.Error is { } error) return (string.Format(Strings.StatusError, error), ChipKind.Error);
        if (info.RestartRequired) return (Strings.StatusRestart, ChipKind.Warning);
        if (!info.IsInstalled)
        {
            return info.IsCompatible
                ? (Strings.StatusNotInstalled, ChipKind.Neutral)
                : (string.Format(Strings.StatusIncompatible, Strings.IncompatibleReason), ChipKind.Warning);
        }

        if (info.HasUpdate) return (Strings.StatusUpdate, ChipKind.Accent);
        return info.IsEnabled ? (Strings.StatusEnabled, ChipKind.Good) : (Strings.StatusDisabled, ChipKind.Neutral);
    }

    public Task InstallAsync() => RunAsync(progress => _modules.InstallAsync(Id, progress, _owner.OperationToken), showsProgress: true);

    public Task UpdateAsync() => RunAsync(progress => _modules.UpdateAsync(Id, progress, _owner.OperationToken), showsProgress: true);

    public Task ToggleAsync() =>
        RunAsync(_ => _isEnabledModule ? _modules.DisableAsync(Id) : _modules.EnableAsync(Id), showsProgress: false);

    public Task RollbackAsync() => RunAsync(_ => _modules.RollbackAsync(Id), showsProgress: false);

    public Task UninstallAsync()
    {
        if (!_owner.Confirm(string.Format(Strings.UninstallConfirm, Name))) return Task.CompletedTask;
        return RunAsync(_ => _modules.UninstallAsync(Id), showsProgress: false);
    }

    private async Task RunAsync(Func<IProgress<double>?, Task<OperationResult>> operation, bool showsProgress)
    {
        if (_owner.AnyOperationRunning) return;

        Message = null;
        MessageIsError = false;
        ProgressIsIndeterminate = true;
        ProgressValue = 0;
        IsBusy = true;
        _owner.OperationStarted();

        IProgress<double>? progress = showsProgress
            ? new Progress<double>(value =>
            {
                ProgressIsIndeterminate = false;
                ProgressValue = Math.Clamp(value, 0, 1);
            })
            : null;

        try
        {
            var result = await operation(progress);
            if (!result.Success)
            {
                MessageIsError = true;
                Message = string.Format(Strings.OperationFailed, result.Error ?? "?");
            }
            else if (result.RestartRequired)
            {
                Message = Strings.OperationNeedsRestart;
            }
        }
        catch (Exception exception)
        {
            _owner.Log.Error($"Операція над модулем {Id} не вдалася", exception);
            MessageIsError = true;
            Message = string.Format(Strings.OperationFailed, exception.Message);
        }
        finally
        {
            IsBusy = false;
            _owner.OperationFinished();
            _owner.Refresh();
        }
    }

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
