using System.Globalization;
using System.Text;
using Kastom.Sdk.Services;

namespace Kastom.Core.Logging;

/// <summary>Лог у %LocalAppData%\Kastom\logs; category — "launcher" або id модуля.</summary>
public sealed class FileLogger(KastomPaths paths, string category) : IModuleLogger
{
    private const int RetentionDays = 7;

    // Один замок на процес: усі категорії пишуть в один файл за день.
    private static readonly object Sync = new();

    // Папки, у яких старі логи вже прибрано в цьому процесі.
    private static readonly HashSet<string> CleanedRoots = new(StringComparer.OrdinalIgnoreCase);

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = DateTime.Now;
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] [").Append(category).Append("] ").Append(message)
                .AppendLine();
            if (exception is not null) line.AppendLine(exception.ToString());

            lock (Sync)
            {
                var root = paths.LogsRoot;
                Directory.CreateDirectory(root);
                if (CleanedRoots.Add(root)) DeleteOldLogs(root, now);

                var file = Path.Combine(root, $"kastom-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Лог не має права ламати програму.
        }
    }

    private static void DeleteOldLogs(string root, DateTime now)
    {
        try
        {
            var border = now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(root, "kastom-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < border) File.Delete(file);
                }
                catch
                {
                    // Файл зайнятий — лишається до наступного запуску.
                }
            }
        }
        catch
        {
            // Нічого страшного: старі логи залишаться.
        }
    }
}
