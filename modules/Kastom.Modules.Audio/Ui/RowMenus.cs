using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Меню «⋯» програм і мікрофонів. Пункти збираються щоразу при відкритті: пристроїв і значень слотів тоді вже інші.</summary>
internal static class RowMenus
{
    /// <summary>Вибір виводу програми доступний, коли його ввімкнено в налаштуваннях і вміє бекенд.</summary>
    public static bool CanRoute(MixerContext ctx, AudioApp app) =>
        ctx.Options.PerAppOutput && ctx.Backend.Capabilities.PerAppRouting && !app.IsSystemSounds;

    public static bool HasAppMenu(MixerContext ctx, AudioApp app) => CanRoute(ctx, app) || app.ProcessName is not null;

    public static ContextMenu BuildAppMenu(MixerContext ctx, AudioApp app)
    {
        var menu = new ContextMenu();

        if (CanRoute(ctx, app))
        {
            var output = new MenuItem { Header = MixerStrings.MenuOutput };
            output.Items.Add(Choice(MixerStrings.MenuOutputDefault, app.RoutedDeviceId is null, () => Route(ctx, app, null)));
            foreach (var device in ctx.Snapshot.Outputs)
            {
                var target = device.Id;
                output.Items.Add(Choice(device.Name, app.RoutedDeviceId == target, () => Route(ctx, app, target)));
            }

            menu.Items.Add(output);
        }

        if (app.ProcessName is { } process)
        {
            var assign = new MenuItem { Header = MixerStrings.MenuAssign };
            for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
            {
                var number = slot;
                var current = ctx.Options.AppSlot(number);
                var text = MixerStrings.Format(MixerStrings.MenuAppSlotFormat, number, current.Length == 0 ? MixerStrings.SlotNotSet : current);
                assign.Items.Add(Choice(text, false, () => Assign(ctx, () => ctx.Options.SetAppSlot(number, process))));
            }

            menu.Items.Add(assign);
        }

        return menu;
    }

    public static ContextMenu BuildMicMenu(MixerContext ctx, AudioDevice mic)
    {
        var assign = new MenuItem { Header = MixerStrings.MenuAssign };
        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++)
        {
            var number = slot;
            var current = ctx.Options.MicSlot(number);
            var text = MixerStrings.Format(MixerStrings.MenuMicSlotFormat, number, current.Length == 0 ? MixerStrings.SlotNotSet : current);
            assign.Items.Add(Choice(text, false, () => Assign(ctx, () => ctx.Options.SetMicSlot(number, mic.Name))));
        }

        var menu = new ContextMenu();
        menu.Items.Add(assign);
        return menu;
    }

    public static void Open(FrameworkElement anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static MenuItem Choice(string header, bool isChecked, Action onClick)
    {
        var item = new MenuItem { Header = MixerUi.EscapeMenu(header), IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void Route(MixerContext ctx, AudioApp app, string? targetDeviceId) =>
        _ = ctx.Run(() => ctx.Backend.SetAppOutputAsync(app.DeviceId, app.Key, targetDeviceId), Strings.OsdAppRoutingFailed);

    private static void Assign(MixerContext ctx, Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex)
        {
            ctx.Report(Strings.OsdCommandFailed, ex);
        }
    }
}
