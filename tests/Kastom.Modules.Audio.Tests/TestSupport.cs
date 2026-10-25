using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Kastom.Modules.Audio.Model;
using Kastom.Sdk.Settings;

namespace Kastom.Modules.Audio.Tests;

/// <summary>Налаштування в пам'яті з тими самими правилами, що в лаунчері (типові значення, обрізання повзунка).</summary>
internal sealed class TestSettingsRegistry : ISettingsRegistry
{
    private readonly List<SettingDefinition> _definitions = [];
    private readonly Dictionary<string, object> _values = new();
    private Func<FrameworkElement>? _customPage;

    public IReadOnlyList<SettingDefinition> Definitions => _definitions;

    public Func<FrameworkElement>? CustomPageFactory => _customPage;

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public void Define(SettingDefinition definition)
    {
        if (_definitions.Any(d => d.Key == definition.Key)) throw new ArgumentException($"Ключ '{definition.Key}' повторюється.");
        _definitions.Add(definition);
    }

    public bool GetBool(string key) =>
        _values.TryGetValue(key, out var value) ? (bool)value : Find<ToggleSetting>(key).Default;

    public double GetDouble(string key) =>
        _values.TryGetValue(key, out var value) ? (double)value : Find<RangeSetting>(key).Default;

    public string GetString(string key)
    {
        if (_values.TryGetValue(key, out var value)) return (string)value;
        return Find<SettingDefinition>(key) switch
        {
            TextSetting text => text.Default,
            ChoiceSetting choice => choice.Default,
            _ => throw new ArgumentException($"'{key}' не текстове."),
        };
    }

    public void Set(string key, bool value)
    {
        Find<ToggleSetting>(key);
        Store(key, value);
    }

    public void Set(string key, double value)
    {
        var range = Find<RangeSetting>(key);
        Store(key, Math.Clamp(value, range.Min, range.Max));
    }

    public void Set(string key, string value)
    {
        Find<SettingDefinition>(key);
        Store(key, value);
    }

    public void SetCustomPage(Func<FrameworkElement> factory) => _customPage = factory;

    private T Find<T>(string key) where T : SettingDefinition =>
        _definitions.FirstOrDefault(d => d.Key == key) as T ?? throw new KeyNotFoundException(key);

    private void Store(string key, object value)
    {
        _values[key] = value;
        Changed?.Invoke(this, new SettingChangedEventArgs(key, value));
    }
}

/// <summary>Записує, які сторінки Windows відкривали й про які помилки повідомляли.</summary>
internal sealed class FakeShell : IAudioShell
{
    public List<string> Calls { get; } = [];

    public List<(string Message, Exception? Exception)> Errors { get; } = [];

    public void OpenSoundSettings() => Calls.Add(nameof(OpenSoundSettings));

    public void OpenInputProperties() => Calls.Add(nameof(OpenInputProperties));

    public void OpenOutputProperties() => Calls.Add(nameof(OpenOutputProperties));

    public void OpenAppVolumeSettings() => Calls.Add(nameof(OpenAppVolumeSettings));

    public void OpenTaskbarSettings() => Calls.Add(nameof(OpenTaskbarSettings));

    public void ReportError(string userMessage, Exception? exception) => Errors.Add((userMessage, exception));
}

internal static class TestOptions
{
    /// <summary>Налаштування модуля з типовими значеннями.</summary>
    public static (TestSettingsRegistry Settings, AudioOptions Options) Create()
    {
        var settings = new TestSettingsRegistry();
        AudioSettings.Define(settings);
        return (settings, new AudioOptions(settings));
    }
}

/// <summary>Запуск коду WPF в окремому STA-потоці з власним Dispatcher, без вікон на екрані.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>Виконати все, що чекає в черзі Dispatcher поточного потоку (BeginInvoke, Loaded).</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}

internal static class ModuleFiles
{
    /// <summary>Тека з кодом модуля (module.json, Resources).</summary>
    public static string Directory =>
        typeof(ModuleFiles).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "AudioModuleDirectory").Value!;
}
