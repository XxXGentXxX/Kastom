using System.Windows;
using System.Windows.Controls;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Modules.Template;

// Що міняти:
//  1. Ім'я класу й простору імен — вони мають збігатися з entryType у module.json.
//  2. Ключі, назви й типове поєднання клавіш нижче — на свої.
//  3. Логіку в Execute, StartAsync і StopAsync — на справжню.
// Якщо модуль створено скриптом scripts\new-module.ps1, імена вже підставлено.

/// <summary>Мінімальний модуль: налаштування-перемикач, дія з клавішею й підказкою на екрані, віджет.</summary>
public sealed class TemplateModule : IKastomModule
{
    // Ключ налаштування: латиниця, цифри, дефіс. Користувач бачить лише Title.
    private const string ShowMessageKey = "show-message";

    private IModuleContext? _context;

    // Викликається один раз за запуск лаунчера, у потоці інтерфейсу. Тут лише реєструємо — систему не змінюємо.
    public void Configure(IModuleContext context)
    {
        _context = context;

        context.Settings.Define(new ToggleSetting
        {
            Key = ShowMessageKey,
            Title = "Показувати підказку",
            Description = "Чи показувати підказку на екрані після дії.",
            Default = true,
            // IsQuick = true виносить перемикач у швидку панель лаунчера.
            IsQuick = true,
        });

        context.Actions.Register(new ModuleAction
        {
            // Повний ідентифікатор дії в лаунчері — "<id модуля>/say-hello".
            Id = "say-hello",
            Title = "Привітатися",
            // Лаунчер не призначить це поєднання, якщо воно вже зайняте.
            DefaultHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 'T'),
            // Execute викликається в потоці інтерфейсу: виконуйте швидко, довгу роботу виносьте в Task.Run.
            Execute = _ =>
            {
                if (context.Settings.GetBool(ShowMessageKey))
                    context.Notifications.ShowOsd("Привіт від модуля!");
                return Task.CompletedTask;
            },
        });

        context.Widgets.Register(new WidgetDefinition
        {
            Id = "status",
            Title = "Стан",
            DefaultWidth = 220,
            DefaultHeight = 100,
            CreateContent = CreateStatus,
        });
    }

    // Модуль увімкнено. Викликається у фоновому потоці й має вкластися приблизно в 10 секунд.
    // До інтерфейсу звертайтеся лише через context.Ui.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _context?.Log.Info("Модуль запущено.");
        return Task.CompletedTask;
    }

    // Модуль вимикають або лаунчер закривається: поверніть усе, що змінили в системі.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _context?.Log.Info("Модуль зупинено.");
        return Task.CompletedTask;
    }

    private FrameworkElement CreateStatus()
    {
        var text = new TextBlock
        {
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        void Refresh() => text.Text = _context!.Settings.GetBool(ShowMessageKey) ? "Підказки увімкнено" : "Підказки вимкнено";

        // Подія може прийти не з потоку інтерфейсу, тож оновлюємо через Dispatcher елемента.
        void OnChanged(object? sender, SettingChangedEventArgs e)
        {
            if (e.Key == ShowMessageKey) text.Dispatcher.BeginInvoke(Refresh);
        }

        // Підписка живе, поки віджет на екрані: так закритий віджет не лишається в пам'яті.
        text.Loaded += (_, _) =>
        {
            Refresh();
            _context!.Settings.Changed += OnChanged;
        };
        text.Unloaded += (_, _) => _context!.Settings.Changed -= OnChanged;
        return text;
    }
}
