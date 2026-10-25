using Microsoft.Win32;

namespace Kastom.Launcher.Services;

/// <summary>Автозапуск через HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kastom";

    public string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string == Command;
    }

    public void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            if (key.GetValue(ValueName) as string != Command) key.SetValue(ValueName, Command);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
