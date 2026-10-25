using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kastom.Core.Models;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;
using Kastom.Launcher.Widgets;

namespace Kastom.Launcher.Pages;

/// <summary>Вкладка «Лаунчер»: автозапуск, оновлення, закріплення на панелі задач, віджет, логи, джерело каталогу.</summary>
public partial class LauncherSettingsPage : UserControl, IDisposable
{
    private readonly LauncherServices _services;
    private bool _loading;
    private bool _disposed;

    public LauncherSettingsPage(LauncherServices services)
    {
        _services = services;
        InitializeComponent();
        LoadValues();

        services.Updates.StateChanged += OnUpdatesStateChanged;
        services.Widgets.VisibilityChanged += OnWidgetVisibilityChanged;
        _ = LoadPinAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _services.Updates.StateChanged -= OnUpdatesStateChanged;
        _services.Widgets.VisibilityChanged -= OnWidgetVisibilityChanged;
    }

    /// <summary>Віджет показали чи сховали деінде (трей, клавіша): тримаємо перемикач у відповідності.</summary>
    private void OnWidgetVisibilityChanged(object? sender, string key)
    {
        if (key != WidgetHost.LauncherWidgetKey) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_disposed) return;
            try { WidgetBox.IsChecked = _services.Widgets.IsVisible(WidgetHost.LauncherWidgetKey); }
            catch (Exception) { }
        });
    }

    private void LoadValues()
    {
        _loading = true;
        VersionText.Text = string.Format(Strings.SettingsVersion, _services.LauncherVersion);

        try
        {
            var state = _services.StateStore.State;
            AutostartBox.IsChecked = state.Autostart;
            ModeNotify.IsChecked = state.UpdateMode == UpdateMode.Notify;
            ModeAutomatic.IsChecked = state.UpdateMode == UpdateMode.Automatic;
            CatalogUrlBox.Text = state.CatalogUrlOverride ?? "";
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося прочитати стан лаунчера для сторінки налаштувань", exception);
            ShowMessage(exception.Message, isError: true);
        }

        try
        {
            WidgetBox.IsChecked = _services.Widgets.IsVisible(WidgetHost.LauncherWidgetKey);
        }
        catch (Exception exception)
        {
            _services.Log.Warn($"Не вдалося дізнатися, чи видно віджет: {exception.Message}");
            WidgetBox.IsChecked = false;
        }

        if (_services.Options.CatalogDirectory is { } directory)
        {
            CatalogSourceText.Text = string.Format(Strings.CatalogDevFolder, directory);
            CatalogEditPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            CatalogSourceText.Text = string.Format(Strings.CatalogCurrentUrl, _services.CatalogDescription);
        }

        RefreshUpdateInfo();
        _loading = false;
    }

    private void RefreshUpdateInfo()
    {
        DateTimeOffset? lastCheck = null;
        string? launcherVersion = null;
        try
        {
            lastCheck = _services.Updates.LastCheck;
            launcherVersion = _services.Updates.LauncherAvailableVersion;
        }
        catch (Exception)
        {
            // Служба оновлень недоступна: лишаємо типовий вигляд.
        }

        LastCheckText.Text = lastCheck is { } time
            ? string.Format(Strings.LastCheckFormat, time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Strings.LastCheckNever;

        if (launcherVersion is null)
        {
            UpdateLauncherButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpdateLauncherButton.Content = string.Format(Strings.SettingsUpdateLauncher, launcherVersion);
            UpdateLauncherButton.Visibility = Visibility.Visible;
        }
    }

    private void OnUpdatesStateChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!_disposed) RefreshUpdateInfo();
        });

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.SetResourceReference(
            TextBlock.ForegroundProperty,
            isError ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
        MessageText.Visibility = Visibility.Visible;
    }

    private void ClearMessage() => MessageText.Visibility = Visibility.Collapsed;

    // ---------- Запуск ----------

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ClearMessage();
        var enabled = AutostartBox.IsChecked == true;
        try
        {
            _services.Autostart.Apply(enabled);
            _services.StateStore.State.Autostart = enabled;
            _services.StateStore.Save();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося змінити автозапуск", exception);
            AutostartBox.IsChecked = !enabled;
            ShowMessage(string.Format(Strings.AutostartFailed, exception.Message), isError: true);
        }
    }

    // ---------- Оновлення ----------

    private void OnUpdateModeClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ClearMessage();
        try
        {
            _services.StateStore.State.UpdateMode =
                ModeAutomatic.IsChecked == true ? UpdateMode.Automatic : UpdateMode.Notify;
            _services.StateStore.Save();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося зберегти режим оновлень", exception);
            ShowMessage(exception.Message, isError: true);
        }
    }

    private async void OnCheckNowClick(object sender, RoutedEventArgs e)
    {
        ClearMessage();
        CheckNowButton.IsEnabled = false;
        try
        {
            await _services.Updates.CheckNowAsync();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося перевірити оновлення", exception);
            ShowMessage(exception.Message, isError: true);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
            RefreshUpdateInfo();
        }
    }

    private async void OnUpdateLauncherClick(object sender, RoutedEventArgs e)
    {
        ClearMessage();
        UpdateLauncherButton.IsEnabled = false;
        ShowMessage(Strings.SettingsUpdatingLauncher, isError: false);
        try
        {
            await _services.Updates.ApplyLauncherUpdateAsync();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося оновити лаунчер", exception);
            ShowMessage(string.Format(Strings.SettingsLauncherUpdateFailed, exception.Message), isError: true);
            UpdateLauncherButton.IsEnabled = true;
        }
    }

    // ---------- Панель задач ----------

    private async Task LoadPinAsync()
    {
        try
        {
            var manager = global::Windows.UI.Shell.TaskbarManager.GetDefault();
            if (manager.IsSupported && manager.IsPinningAllowed && !await manager.IsCurrentAppPinnedAsync())
                PinPanel.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            // Немає підтримки (не Windows 11, немає ярлика в «Пуску»): кнопку не показуємо.
            Debug.WriteLine($"[Kastom] Закріплення на панелі задач недоступне: {exception.Message}");
        }
    }

    private async void OnPinClick(object sender, RoutedEventArgs e)
    {
        ClearMessage();
        PinButton.IsEnabled = false;
        try
        {
            var pinned = await global::Windows.UI.Shell.TaskbarManager.GetDefault().RequestPinCurrentAppAsync();
            ShowMessage(pinned ? Strings.PinDone : Strings.PinNotDone, isError: !pinned);
            if (pinned) PinPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося закріпити на панелі задач", exception);
            ShowMessage(string.Format(Strings.PinFailed, exception.Message), isError: true);
        }
        finally
        {
            PinButton.IsEnabled = true;
        }
    }

    // ---------- Віджет, логи, каталог ----------

    private void OnWidgetClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ClearMessage();
        var show = WidgetBox.IsChecked == true;
        try
        {
            if (show) _services.Widgets.Show(WidgetHost.LauncherWidgetKey);
            else _services.Widgets.Hide(WidgetHost.LauncherWidgetKey);
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося перемкнути віджет лаунчера", exception);
            WidgetBox.IsChecked = !show;
            ShowMessage(exception.Message, isError: true);
        }
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        ClearMessage();
        try
        {
            Directory.CreateDirectory(_services.Paths.LogsRoot);
            Process.Start(new ProcessStartInfo(_services.Paths.LogsRoot) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowMessage(string.Format(Strings.LogsOpenFailed, exception.Message), isError: true);
        }
    }

    private void OnCatalogApplyClick(object sender, RoutedEventArgs e)
    {
        ClearMessage();
        var text = CatalogUrlBox.Text.Trim();
        if (text.Length > 0 &&
            !(Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            ShowMessage(Strings.CatalogInvalidUrl, isError: true);
            return;
        }

        try
        {
            _services.StateStore.State.CatalogUrlOverride = text.Length == 0 ? null : text;
            _services.StateStore.Save();
            ShowMessage(Strings.CatalogApplyNote, isError: false);
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося зберегти адресу каталогу", exception);
            ShowMessage(exception.Message, isError: true);
        }
    }
}
