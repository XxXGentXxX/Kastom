using System.Text.Json;
using System.Windows;
using Kastom.Sdk.Settings;

namespace Kastom.Core.Runtime;

/// <summary>Налаштування одного модуля; значення лежать у settings\modules\&lt;id&gt;.json.</summary>
internal sealed class ModuleSettingsRegistry(KastomPaths paths, string moduleId) : ISettingsRegistry
{
    private readonly object _sync = new();
    private readonly List<SettingDefinition> _definitions = [];
    private readonly Dictionary<string, SettingDefinition> _byKey = new();
    private Dictionary<string, JsonElement>? _values;
    private Func<FrameworkElement>? _customPage;

    public IReadOnlyList<SettingDefinition> Definitions
    {
        get
        {
            lock (_sync) return _definitions.ToArray();
        }
    }

    public Func<FrameworkElement>? CustomPageFactory
    {
        get
        {
            lock (_sync) return _customPage;
        }
    }

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public void Define(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IdPattern.IsValid(definition.Key))
            throw new ArgumentException($"Неправильний ключ налаштування: '{definition.Key}'.", nameof(definition));

        lock (_sync)
        {
            if (!_byKey.TryAdd(definition.Key, definition))
                throw new ArgumentException($"Налаштування '{definition.Key}' уже описано.", nameof(definition));
            _definitions.Add(definition);
        }
    }

    public void SetCustomPage(Func<FrameworkElement> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync) _customPage = factory;
    }

    public bool GetBool(string key)
    {
        lock (_sync)
        {
            var definition = Require<ToggleSetting>(key);
            return Stored(key) is { ValueKind: JsonValueKind.True or JsonValueKind.False } stored
                ? stored.GetBoolean()
                : definition.Default;
        }
    }

    public double GetDouble(string key)
    {
        lock (_sync)
        {
            var definition = Require<RangeSetting>(key);
            return Stored(key) is { ValueKind: JsonValueKind.Number } stored && stored.TryGetDouble(out var value)
                ? Normalize(definition, value)
                : definition.Default;
        }
    }

    public string GetString(string key)
    {
        lock (_sync)
        {
            var definition = Find(key);
            var fallback = definition switch
            {
                ChoiceSetting choice => choice.Default,
                TextSetting text => text.Default,
                _ => throw new ArgumentException($"Налаштування '{key}' не зберігає текст."),
            };

            if (Stored(key) is not { ValueKind: JsonValueKind.String } stored) return fallback;
            var value = stored.GetString() ?? fallback;
            return definition is ChoiceSetting options && !options.Options.Any(o => o.Value == value) ? fallback : value;
        }
    }

    public void Set(string key, bool value)
    {
        lock (_sync)
        {
            Require<ToggleSetting>(key);
            Store(key, value);
        }

        Changed?.Invoke(this, new SettingChangedEventArgs(key, value));
    }

    public void Set(string key, double value)
    {
        double normalized;
        lock (_sync)
        {
            var definition = Require<RangeSetting>(key);
            normalized = Normalize(definition, value);
            Store(key, normalized);
        }

        Changed?.Invoke(this, new SettingChangedEventArgs(key, normalized));
    }

    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            var definition = Find(key);
            switch (definition)
            {
                case ChoiceSetting choice when choice.Options.Any(o => o.Value == value):
                    break;
                case ChoiceSetting:
                    throw new ArgumentException($"Значення '{value}' немає серед варіантів налаштування '{key}'.");
                case TextSetting:
                    break;
                default:
                    throw new ArgumentException($"Налаштування '{key}' не приймає текст.");
            }

            Store(key, value);
        }

        Changed?.Invoke(this, new SettingChangedEventArgs(key, value));
    }

    // Обрізає до Min..Max і округлює до кроку, що рахується від Min.
    private static double Normalize(RangeSetting definition, double value)
    {
        var result = Math.Clamp(value, definition.Min, definition.Max);
        if (definition.Step > 0)
        {
            var steps = Math.Round((result - definition.Min) / definition.Step, MidpointRounding.AwayFromZero);
            result = Math.Clamp(Math.Round(definition.Min + steps * definition.Step, 10), definition.Min, definition.Max);
        }

        return result;
    }

    private SettingDefinition Find(string key) =>
        _byKey.TryGetValue(key, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Налаштування '{key}' не описано.");

    private T Require<T>(string key) where T : SettingDefinition =>
        Find(key) as T ?? throw new ArgumentException($"Налаштування '{key}' має інший тип.");

    private JsonElement? Stored(string key) => Values().TryGetValue(key, out var value) ? value : null;

    private Dictionary<string, JsonElement> Values() => _values ??= LoadValues();

    private Dictionary<string, JsonElement> LoadValues()
    {
        var file = paths.ModuleSettingsFile(moduleId);
        if (!File.Exists(file)) return new Dictionary<string, JsonElement>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file), KastomJson.Options)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            // Пошкоджений файл — повертаємося до типових значень; наступний запис його замінить.
            return new Dictionary<string, JsonElement>();
        }
    }

    // Спершу файл, потім пам'ять: якщо запис не вдався, інтерфейс не покаже значення, якого немає на диску.
    private void Store(string key, object value)
    {
        var updated = new Dictionary<string, JsonElement>(Values())
        {
            [key] = JsonSerializer.SerializeToElement(value, KastomJson.Options),
        };
        AtomicFile.WriteAllText(paths.ModuleSettingsFile(moduleId), JsonSerializer.Serialize(updated, KastomJson.Options));
        _values = updated;
    }
}
