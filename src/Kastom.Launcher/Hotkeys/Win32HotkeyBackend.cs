using System.Runtime.InteropServices;
using System.Windows.Interop;
using Kastom.Core.Abstractions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Hotkeys;

/// <summary>RegisterHotKey через приховане вікно повідомлень. Створювати в потоці інтерфейсу.</summary>
public sealed class Win32HotkeyBackend : IHotkeyBackend
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();
    private bool _disposed;

    public Win32HotkeyBackend()
    {
        var parameters = new HwndSourceParameters("KastomHotkeys")
        {
            ParentWindow = HwndMessage,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler<int>? Pressed;

    public bool TryRegister(int id, HotkeyGesture gesture)
    {
        if (_disposed) return false;
        if (_registered.Contains(id)) Unregister(id);

        var modifiers = (uint)gesture.Modifiers | ModNoRepeat;
        if (!RegisterHotKey(_source.Handle, id, modifiers, (uint)gesture.VirtualKey)) return false;

        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_disposed || !_registered.Remove(id)) return;
        UnregisterHotKey(_source.Handle, id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var id in _registered) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            handled = true;
            Pressed?.Invoke(this, (int)wParam);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
