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

    public static bool HasAppMenu(MixerContext ctx, AudioApp app) =>
        CanRoute(ctx, app) || app.ProcessName is not null || app.ExePath is not null;

    public static ContextMenu BuildAppMenu(MixerContext ctx, AudioApp app)
    {
        var menu = new ContextMenu();

        // «Системні звуки» й програми без шляху до exe відкривати нічим.
        if (app.ExePath is not null)
        {
            menu.Items.Add(Choice(MixerStrings.MenuOpenApp, false, () => Guarded(ctx, () => ctx.Host.Shell.OpenApp(app))));
            menu.Items.Add(Choice(MixerStrings.MenuRevealApp, false, () => Guarded(ctx, () => ctx.Host.Shell.RevealApp(app))));
            if (CanRoute(ctx, app) || app.ProcessName is not null) menu.Items.Add(new Separator());
        }

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
                assign.Items.Add(Choice(text, false, () => Guarded(ctx, () => ctx.Options.SetAppSlot(number, process))));
            }

            menu.Items.Add(assign);
        }

        return menu;
    }

    /// <summary>Меню картки пристрою виводу: програма, чию назву несе пристрій, і параметри звуку Windows.</summary>
    public static ContextMenu BuildOutputMenu(MixerContext ctx, AudioDevice device)
    {
        var menu = new ContextMenu();
        AddOpenDeviceApp(ctx, menu, device);
        AddSoundSettings(ctx, menu);
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
            assign.Items.Add(Choice(text, false, () => Guarded(ctx, () => ctx.Options.SetMicSlot(number, mic.Name))));
        }

        var menu = new ContextMenu();
        AddOpenDeviceApp(ctx, menu, mic);
        menu.Items.Add(assign);
        AddSoundSettings(ctx, menu);
        return menu;
    }

    public static void Open(FrameworkElement anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // «Speakers (NVIDIA Broadcast)» → пункт «Відкрити NVIDIA Broadcast», якщо така програма звучить і її шлях відомий.
    private static void AddOpenDeviceApp(MixerContext ctx, ContextMenu menu, AudioDevice device)
    {
        if (ctx.Snapshot.FindAppOfDevice(device) is not { } app) return;

        var name = string.IsNullOrWhiteSpace(app.Name) ? app.ProcessName ?? "" : app.Name;
        var text = MixerStrings.Format(MixerStrings.MenuOpenAppFormat, name);
        menu.Items.Add(Choice(text, false, () => Guarded(ctx, () => ctx.Host.Shell.OpenApp(app))));
    }

    private static void AddSoundSettings(MixerContext ctx, ContextMenu menu)
    {
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Choice(MixerStrings.MenuSoundSettings, false, () => Guarded(ctx, ctx.Host.Shell.OpenSoundSettings)));
    }

    private static MenuItem Choice(string header, bool isChecked, Action onClick)
    {
        var item = new MenuItem { Header = MixerUi.EscapeMenu(header), IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void Route(MixerContext ctx, AudioApp app, string? targetDeviceId) =>
        _ = ctx.Run(() => ctx.Backend.SetAppOutputAsync(app.DeviceId, app.Key, targetDeviceId), Strings.OsdAppRoutingFailed);

    private static void Guarded(MixerContext ctx, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ctx.Report(Strings.OsdCommandFailed, ex);
        }
    }
}
