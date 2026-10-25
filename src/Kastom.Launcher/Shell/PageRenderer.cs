using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kastom.Core.Models;
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

    /// <summary>
    /// Розміри вікна в DIP і масштаб екрана. 1280×688 при 150 % і 1536×824 при 125 % — уся робоча область монітора
    /// 1920×1080 за такого масштабу: так видно, чи вміщається сторінка. Розкладка лишається в DIP (округлення
    /// за 96 dpi), масштаб лише дає більше пікселів на знімку.
    /// </summary>
    private static readonly (int Width, int Height, double Scale)[] Sizes =
        [(900, 600, 1.0), (1600, 1000, 1.0), (1536, 824, 1.25), (1280, 688, 1.5)];

    private static readonly (string Name, LauncherTheme Theme)[] Themes = [("light", LauncherTheme.Light), ("dark", LauncherTheme.Dark)];

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

        foreach (var (themeName, theme) in Themes)
        {
            LauncherThemes.Apply(theme);
            var window = new MainWindow(services, () => { });
            PlaceOffScreen(window, Sizes.Max(size => size.Width));
            window.Show();

            foreach (var (width, height, scale) in Sizes)
            {
                window.Width = width;
                window.Height = height;
                var suffix = $"{width}x{height}{(scale == 1.0 ? "" : $"@{scale * 100:0}")}-{themeName}";

                foreach (var (name, page) in Pages)
                {
                    window.NavigateTo(page);
                    failures += await SaveWindowAsync(dispatcher, window, scale, Path.Combine(outputDirectory, $"{name}-{suffix}.png"), name);
                }

                if (FirstModuleId(services) is { } moduleId)
                {
                    window.OpenModulePage(moduleId);
                    failures += await SaveWindowAsync(dispatcher, window, scale, Path.Combine(outputDirectory, $"module-{moduleId}-{suffix}.png"), moduleId);

                    // Вкладка «Налаштування»: налаштування, гарячі клавіші, віджети, дії.
                    window.OpenModuleSettings(moduleId);
                    failures += await SaveWindowAsync(
                        dispatcher, window, scale, Path.Combine(outputDirectory, $"module-{moduleId}-settings-{suffix}.png"), $"{moduleId}-settings");
                }
            }

            window.Close();

            failures += await SaveContentAsync(
                dispatcher, () => new ModulesQuickView(services.Modules, services.Actions, _ => { }),
                Path.Combine(outputDirectory, $"quickpanel-340x480-{themeName}.png"), "quickpanel");
            failures += await SaveContentAsync(
                dispatcher, () => new ModulesQuickView(services.Modules, services.Actions, _ => { }),
                Path.Combine(outputDirectory, $"widget-340x480@150-{themeName}.png"), "widget", 1.5);
        }

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

    private static async Task<int> SaveWindowAsync(Dispatcher dispatcher, Window window, double scale, string path, string label)
    {
        await SettleAsync(dispatcher);
        try
        {
            window.UpdateLayout();
            SavePng(window.Background, (FrameworkElement)window.Content, path, scale);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(string.Format(Strings.RenderFailed, label, exception.Message));
            return 1;
        }
    }

    private static async Task<int> SaveContentAsync(Dispatcher dispatcher, Func<FrameworkElement> create, string path, string label, double scale = 1.0)
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
            SavePng(host.Background, (FrameworkElement)host.Content, path, scale);
            host.Close();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(string.Format(Strings.RenderFailed, label, exception.Message));
            return 1;
        }
    }

    /// <summary>
    /// Знімок елемента на тлі вікна. При масштабі 1 розмір файлу збігається з розміром у назві (96 dpi);
    /// при 125–150 % — більше фізичних пікселів на ту саму розкладку, як на екрані з таким масштабом.
    /// </summary>
    private static void SavePng(Brush background, FrameworkElement content, string path, double scale)
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

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
