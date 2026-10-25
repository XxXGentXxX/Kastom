using System.Windows;
using System.Windows.Controls;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Modules.Demo;

/// <summary>Демо-модуль: перевіряє контракт — налаштування, дію з клавішею і віджет.</summary>
public sealed class DemoModule : IKastomModule
{
    private IModuleContext? _context;
    private int _count;

    // Усі відкриті віджети оновлюються разом.
    private event Action<int>? CountChanged;

    public void Configure(IModuleContext context)
    {
        _context = context;

        context.Settings.Define(new ToggleSetting
        {
            Key = "show-greeting",
            Title = "Показувати привітання",
            Default = true,
            IsQuick = true,
        });
        context.Settings.Define(new RangeSetting
        {
            Key = "intensity",
            Title = "Інтенсивність",
            Min = 0,
            Max = 100,
            Step = 5,
            Default = 50,
            Unit = "%",
            IsQuick = true,
        });
        context.Settings.Define(new ChoiceSetting
        {
            Key = "accent",
            Title = "Колір акценту",
            Options =
            [
                new ChoiceOption("blue", "Синій"),
                new ChoiceOption("green", "Зелений"),
                new ChoiceOption("orange", "Помаранчевий"),
            ],
            Default = "blue",
        });
        context.Settings.Define(new ButtonSetting
        {
            Key = "say-hello",
            Title = "Привітати",
            ActionId = "hello",
        });

        context.Actions.Register(new ModuleAction
        {
            Id = "hello",
            Title = "Привітання",
            DefaultHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 'H'),
            Execute = _ =>
            {
                if (context.Settings.GetBool("show-greeting"))
                    context.Notifications.ShowOsd("Привіт від демо-модуля!");
                return Task.CompletedTask;
            },
        });
        context.Actions.Register(new ModuleAction
        {
            Id = "count",
            Title = "Лічильник +1",
            Execute = _ =>
            {
                Increment();
                return Task.CompletedTask;
            },
        });

        context.Widgets.Register(new WidgetDefinition
        {
            Id = "counter",
            Title = "Лічильник",
            DefaultWidth = 220,
            DefaultHeight = 120,
            CreateContent = CreateCounter,
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _context?.Log.Info("Демо-модуль запущено.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _context?.Log.Info("Демо-модуль зупинено.");
        return Task.CompletedTask;
    }

    private void Increment()
    {
        _count++;
        CountChanged?.Invoke(_count);
    }

    private FrameworkElement CreateCounter()
    {
        var number = new TextBlock
        {
            Text = _count.ToString(),
            FontSize = 40,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var button = new Button
        {
            Content = "+1",
            MinWidth = 64,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        button.Click += (_, _) => Increment();

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { number, button },
        };

        // Підписка живе, поки віджет на екрані: так закритий віджет не лишається в пам'яті.
        void OnCountChanged(int value) => number.Text = value.ToString();
        panel.Loaded += (_, _) =>
        {
            number.Text = _count.ToString();
            CountChanged += OnCountChanged;
        };
        panel.Unloaded += (_, _) => CountChanged -= OnCountChanged;
        return panel;
    }
}
