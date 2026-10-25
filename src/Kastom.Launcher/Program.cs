using Kastom.Core.Runtime;
using Kastom.Launcher.Services;
using Velopack;

namespace Kastom.Launcher;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack першим: він обробляє службові запуски інсталятора й оновлювача (--veloapp-*).
        VelopackApp.Build()
            // Оновлення ставить лише VelopackLauncherUpdater, після перевірки SHA-256 за підписаним каталогом.
            // Типово Velopack сам ставить при старті будь-який новіший пакет із теки packages — це обійшло б перевірку.
            .SetAutoApplyOnStartup(false)
            // Перед видаленням прибираємо автозапуск, щоб у реєстрі не лишився запис на неіснуючий файл.
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { new AutostartService().Apply(false); }
                catch (Exception) { }
            })
            .Run();

        var options = LaunchOptions.Parse(args);
        if (options.Error is { } error)
        {
            // Без вікна: неправильні аргументи бувають лише в перевірках розробника.
            Console.Error.WriteLine($"Kastom: {error}");
            return 2;
        }

        // Режим знімків працює поруч зі справжнім лаунчером і не бере участі в «одному екземплярі».
        SingleInstance? instance = null;
        if (options.RenderPagesDirectory is null)
        {
            instance = SingleInstance.TryAcquire(options.DataDirectory);
            if (instance is null)
            {
                // Запуск з --minimized (автозапуск) вікна не просить.
                if (!options.Minimized) SingleInstance.SignalExisting("show", options.DataDirectory);
                return 0;
            }
        }

        try
        {
            var app = new App(options, instance);
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            instance?.Dispose();
        }
    }
}
