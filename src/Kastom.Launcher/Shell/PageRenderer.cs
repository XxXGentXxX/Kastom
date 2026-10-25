using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;
using Kastom.Launcher.Widgets;

namespace Kastom.Launcher.Shell;

/// <summary>Режим --render-pages: знімки сторінок у PNG для перевірки вигляду без ручного відкривання вікон.</summary>
public static class PageRenderer
{
    private static readonly (string Name, NavPage Page)[] Pages =
    [
        ("modules", NavPage.Modules),
        ("module-settings", NavPage.ModuleSettings),
        ("hotkeys", NavPage.Hotkeys),
        ("launcher", NavPage.Launcher),
    ];

    private static readonly (int Width, int Height)[] Sizes = [(900, 600), (1600, 1000)];

    /// <summary>Повертає код виходу: 0 — усе збережено.</summary>
    public static async Task<int> RunAsync(LauncherServices services, string outputDirectory)
    {
        var failures = 0;
        var dispatcher = Application.Current.Dispatcher;
        Directory.CreateDirectory(outputDirectory);

        // З локальним каталогом розробника підтягуємо його і ставимо модулі (у тимчасову теку цього режиму),
        // щоб на знімках були справжні картки, сторінки модулів і швидкі налаштування.
        if (services.Options.CatalogDirectory is not null)
        {
            await TryRefreshCatalogAsync(services);
            await InstallCatalogModulesAsync(services, outputDirectory);
        }

        var window = new MainWindow(services, () => { });
        PlaceOffScreen(window, Sizes.Max(size => size.Width));
        window.Show();

        foreach (var (width, height) in Sizes)
        {
            window.Width = width;
            window.Height = height;

            foreach (var (name, page) in Pages)
            {
                window.NavigateTo(page);
                failures += await SaveWindowAsync(dispatcher, window, Path.Combine(outputDirectory, $"{name}-{width}x{height}.png"), name);
            }

            if (FirstModuleId(services) is { } moduleId)
            {
                window.OpenModulePage(moduleId);
                failures += await SaveWindowAsync(dispatcher, window, Path.Combine(outputDirectory, $"module-{moduleId}-{width}x{height}.png"), moduleId);
            }
        }

        window.Close();

        failures += await SaveContentAsync(
            dispatcher, () => new ModulesQuickView(services.Modules, services.Actions, _ => { }),
            Path.Combine(outputDirectory, "quickpanel-340x480.png"), "quickpanel");
        failures += await SaveContentAsync(
            dispatcher, () => new ModulesQuickView(services.Modules, services.Actions, _ => { }),
            Path.Combine(outputDirectory, "widget-340x480.png"), "widget");

        return failures == 0 ? 0 : 1;
    }

    private static async Task TryRefreshCatalogAsync(LauncherServices services)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await services.Updates.CheckNowAsync(cts.Token);
        }
        catch (Exception first)
        {
            try
            {
                await services.Catalog.RefreshAsync(cts.Token);
            }
            catch (Exception second)
            {
                Console.Error.WriteLine($"Каталог не прочитано: {first.Message}; {second.Message}");
            }
        }
    }

    /// <summary>Встановити всі сумісні модулі каталогу й записати стан кожного в render-report.txt.</summary>
    private static async Task InstallCatalogModulesAsync(LauncherServices services, string outputDirectory)
    {
        var report = new List<string>();
        try
        {
            var fetch = await services.Catalog.RefreshAsync(CancellationToken.None);
            report.Add($"catalog {services.Options.CatalogDirectory}: {fetch.Status} {fetch.Error} modules={services.Catalog.Current?.Modules.Count}");

            foreach (var module in services.Modules.Modules.Where(m => !m.IsInstalled && m.IsCompatible).ToList())
            {
                var result = await services.Modules.InstallAsync(module.Id);
                report.Add($"install {module.Id}: {(result.Success ? "ok" : "FAIL " + result.Error)}{(result.RestartRequired ? " (restart)" : "")}");
            }

            foreach (var module in services.Modules.Modules)
            {
                var quick = services.Modules.GetSettings(module.Id)?.Definitions.Count(d => d.IsQuick) ?? -1;
                report.Add($"state {module.Id}: installed={module.InstalledVersion} running={module.IsRunning} enabled={module.IsEnabled} quickSettings={quick} error={module.Error}");
            }

            report.AddRange(services.Actions.All.Select(a => $"action {a.GlobalId}: {a.Action.Title}"));
        }
        catch (Exception exception)
        {
            report.Add("EXCEPTION " + exception);
        }

        await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "render-report.txt"), report);
    }

    private static string? FirstModuleId(LauncherServices services)
    {
        try
        {
            return services.Modules.Modules.FirstOrDefault()?.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Лівіше за весь робочий стіл: нічого не мигає на екранах і не забирає фокус.</summary>
    private static void PlaceOffScreen(Window window, double maxWidth)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = SystemParameters.VirtualScreenLeft - maxWidth - 200;
        window.Top = SystemParameters.VirtualScreenTop;
        window.Topmost = false;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    /// <summary>Дати інтерфейсу домалюватися: Loaded, коротка пауза для асинхронних сторінок, ContextIdle.</summary>
    private static async Task SettleAsync(Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Delay(300);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static async Task<int> SaveWindowAsync(Dispatcher dispatcher, Window window, string path, string label)
    {
        await SettleAsync(dispatcher);
        try
        {
            window.UpdateLayout();
            SavePng(window.Background, (FrameworkElement)window.Content, path);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(string.Format(Strings.RenderFailed, label, exception.Message));
            return 1;
        }
    }

    private static async Task<int> SaveContentAsync(Dispatcher dispatcher, Func<FrameworkElement> create, string path, string label)
    {
        try
        {
            var host = new Window
            {
                Width = 340,
                Height = 480,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Content = create(),
            };
            host.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            PlaceOffScreen(host, host.Width);
            host.Show();
            await SettleAsync(dispatcher);
            host.UpdateLayout();
            SavePng(host.Background, (FrameworkElement)host.Content, path);
            host.Close();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(string.Format(Strings.RenderFailed, label, exception.Message));
            return 1;
        }
    }

    /// <summary>Знімок елемента на тлі вікна, у логічних пікселях (96 dpi): розмір файлу збігається з розміром у назві.</summary>
    private static void SavePng(Brush background, FrameworkElement content, string path)
    {
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var area = new Rect(0, 0, width, height);
            context.DrawRectangle(background, null, area);
            context.DrawRectangle(
                new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, area);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
