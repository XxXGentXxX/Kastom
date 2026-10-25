using Kastom.Core.Abstractions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Services;

/// <summary>Бекенд без Win32: для режиму --render-pages, щоб не займати справжні поєднання.</summary>
public sealed class NullHotkeyBackend : IHotkeyBackend
{
    public bool TryRegister(int id, HotkeyGesture gesture) => true;

    public void Unregister(int id)
    {
    }

    public event EventHandler<int>? Pressed { add { } remove { } }

    public void Dispose()
    {
    }
}
