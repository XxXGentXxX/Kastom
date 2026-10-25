using System.Globalization;
using System.Resources;

namespace Kastom.Core.Hotkeys;

/// <summary>Тексти повідомлень менеджера клавіш (HotkeyMessages.resx).</summary>
internal static class HotkeyMessages
{
    private static readonly ResourceManager Manager = new("Kastom.Core.Hotkeys.HotkeyMessages", typeof(HotkeyMessages).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string UnknownAction => Get(nameof(UnknownAction));

    public static string InvalidGesture => Get(nameof(InvalidGesture));

    public static string NeedModifier => Get(nameof(NeedModifier));

    public static string TakenBySystem => Get(nameof(TakenBySystem));

    public static string ResetNoDefault => Get(nameof(ResetNoDefault));
}
