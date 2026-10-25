using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kastom.Modules.Audio.Fake;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Ідентифікатори демо-знімка <see cref="FakeAudioBackend.DemoSnapshot"/>.</summary>
internal static class DemoIds
{
    public const string Headphones = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}";
    public const string Speakers = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}";
    public const string Monitor = "{0.0.0.00000000}.{33333333-3333-3333-3333-333333333333}";
    public const string HeadsetMic = "{0.0.1.00000000}.{44444444-4444-4444-4444-444444444444}";
    public const string WebcamMic = "{0.0.1.00000000}.{55555555-5555-5555-5555-555555555555}";
    public const string ChromeKey = @"c:\program files\google\chrome\application\chrome.exe";
    public const string SpotifyKey = @"c:\users\user\appdata\roaming\spotify\spotify.exe";
}

/// <summary>Мікшер на фальшивому бекенді, без вікна: події Loaded без вікна не приходять, тож Attach викликаємо самі.</summary>
internal sealed class MixerFixture
{
    private MixerFixture(MixerMode mode, bool start, bool attach, Action<TestSettingsRegistry>? configure)
    {
        Backend = FakeAudioBackend.CreateDemo();
        (Settings, Options) = TestOptions.Create();
        configure?.Invoke(Settings);
        Shell = new FakeShell();
        Host = new MixerHost(Backend, Options, Shell);
        if (start) Backend.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        View = new MixerView(Host, mode);
        if (attach) View.Attach();
    }

    public FakeAudioBackend Backend { get; }

    public TestSettingsRegistry Settings { get; }

    public AudioOptions Options { get; }

    public FakeShell Shell { get; }

    public MixerHost Host { get; }

    public MixerView View { get; }

    public static MixerFixture Create(
        MixerMode mode = MixerMode.Page,
        bool start = true,
        bool attach = true,
        Action<TestSettingsRegistry>? configure = null) => new(mode, start, attach, configure);

    public DeviceCard Output(string id) => View.OutputCards.First(c => c.Id == id);

    public DeviceCard Input(string id) => View.InputCards.First(c => c.Id == id);

    public AppRow App(string deviceId, string key) => Output(deviceId).Apps.First(a => a.App.Key == key);

    /// <summary>Знімок бекенда з однією зміненою програмою.</summary>
    public AudioSnapshot WithApp(string deviceId, string key, Func<AudioApp, AudioApp> change) =>
        Backend.Current with
        {
            Devices = Backend.Current.Devices
                .Select(d => d.Id != deviceId ? d : d with { Apps = d.Apps.Select(a => a.Key == key ? change(a) : a).ToArray() })
                .ToArray(),
        };
}

internal static class UiTestExtensions
{
    public static void Press(this ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    public static void Press(this MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public static void DragStarted(this UIElement element) =>
        element.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });

    public static void DragCompleted(this UIElement element) =>
        element.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });

    public static MouseWheelEventArgs Wheel(this UIElement element, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent };
        element.RaiseEvent(args);
        return args;
    }

    public static bool IsDescendantOfMicSettings(this DependencyObject element)
    {
        for (var parent = LogicalTreeHelper.GetParent(element); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is MicSettingsPanel) return true;
        }

        return false;
    }

    public static IEnumerable<T> Descendants<T>(this DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var inner in child.Descendants<T>()) yield return inner;
        }
    }

    public static IEnumerable<string> Headers(this ItemsControl menu) =>
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header);

    public static MenuItem Item(this ItemsControl menu, string header) =>
        menu.Items.OfType<MenuItem>().First(i => (string)i.Header == header);
}
