using System.Diagnostics;
using Kastom.Sdk.Services;

namespace Kastom.Launcher.Services;

/// <summary>Лог, що ніколи не кидає винятків: збій запису не має валити лаунчер.</summary>
public sealed class SafeLogger(IModuleLogger inner) : IModuleLogger
{
    public void Info(string message) => Run(() => inner.Info(message), message);

    public void Warn(string message) => Run(() => inner.Warn(message), message);

    public void Error(string message, Exception? exception = null) =>
        Run(() => inner.Error(message, exception), exception is null ? message : $"{message}: {exception}");

    private static void Run(Action write, string fallback)
    {
        try
        {
            write();
        }
        catch (Exception)
        {
            Debug.WriteLine($"[Kastom] {fallback}");
        }
    }
}
