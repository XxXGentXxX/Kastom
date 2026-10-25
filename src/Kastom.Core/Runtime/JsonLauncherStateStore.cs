using System.Globalization;
using System.Text.Json;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;

namespace Kastom.Core.Runtime;

public sealed class JsonLauncherStateStore(KastomPaths paths) : ILauncherStateStore
{
    private readonly object _sync = new();
    private LauncherState? _state;

    public LauncherState State
    {
        get
        {
            lock (_sync) return _state ??= Load();
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            var json = JsonSerializer.Serialize(_state ??= Load(), KastomJson.Options);
            AtomicFile.WriteAllText(paths.LauncherStateFile, json);
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Saved;

    private LauncherState Load()
    {
        var file = paths.LauncherStateFile;
        if (!File.Exists(file)) return new LauncherState();

        try
        {
            var loaded = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(file), KastomJson.Options);
            if (loaded is not null) return loaded;
        }
        catch (JsonException)
        {
        }

        // Файл пошкоджено: відкладаємо його вбік, щоб його можна було подивитися, і починаємо з типового стану.
        try
        {
            var backup = $"{file}.bad-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            File.Move(file, backup, overwrite: true);
        }
        catch (IOException)
        {
        }

        return new LauncherState();
    }
}
