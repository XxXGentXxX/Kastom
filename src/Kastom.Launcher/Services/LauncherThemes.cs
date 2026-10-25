using System.Windows;
using Kastom.Core.Models;

namespace Kastom.Launcher.Services;

/// <summary>Тема вікон лаунчера (О6): як у Windows, світла чи темна. Діє одразу на всі відкриті вікна.</summary>
public static class LauncherThemes
{
    public static void Apply(LauncherTheme theme)
    {
        if (Application.Current is not { } app) return;
        app.ThemeMode = theme switch
        {
            LauncherTheme.Light => ThemeMode.Light,
            LauncherTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
