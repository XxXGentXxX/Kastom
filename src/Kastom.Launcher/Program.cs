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
            // Перед видаленням прибираємо автозапуск, щоб у реєстрі не лишився запис на неіснуючий файл.
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { new AutostartService().Apply(false); }
                catch (Exception) { }
            })
            .Run();

        var options = LaunchOptions.Parse(args);

        // Режим знімків працює поруч зі справжнім лаунчером і не бере участі в «одному екземплярі».
        SingleInstance? instance = null;
        if (options.RenderPagesDirectory is null)
        {
            instance = SingleInstance.TryAcquire();
            if (instance is null)
            {
                // Запуск з --minimized (автозапуск) вікна не просить.
                if (!options.Minimized) SingleInstance.SignalExisting("show");
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
