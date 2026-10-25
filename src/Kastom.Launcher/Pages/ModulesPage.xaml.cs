using System.Windows;
using System.Windows.Controls;
using Kastom.Launcher.Services;

namespace Kastom.Launcher.Pages;

/// <summary>Вкладка «Модулі»: каталог, оновлення, встановлення й керування модулями.</summary>
public partial class ModulesPage : UserControl, IDisposable
{
    private readonly ModulesPageModel _model;
    private readonly Action _restartLauncher;
    private readonly Action<string> _openModuleSettings;

    public ModulesPage(LauncherServices services, Func<string, bool> confirm, Action restartLauncher, Action<string> openModuleSettings)
    {
        _restartLauncher = restartLauncher;
        _openModuleSettings = openModuleSettings;
        _model = new ModulesPageModel(services, confirm);
        DataContext = _model;
        InitializeComponent();
    }

    public void Dispose() => _model.Dispose();

    private static ModuleCardModel? Card(object sender) => (sender as FrameworkElement)?.DataContext as ModuleCardModel;

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await _model.CheckNowAsync();

    private void OnRestartClick(object sender, RoutedEventArgs e) => _restartLauncher();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) _openModuleSettings(card.Id);
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) await card.InstallAsync();
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) await card.UpdateAsync();
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) await card.ToggleAsync();
    }

    private async void OnRollbackClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) await card.RollbackAsync();
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { } card) await card.UninstallAsync();
    }
}
