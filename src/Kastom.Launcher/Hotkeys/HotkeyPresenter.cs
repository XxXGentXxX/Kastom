using System.Globalization;
using System.Windows.Media;
using Kastom.Core.Abstractions;
using Kastom.Sdk;

namespace Kastom.Launcher.Hotkeys;

/// <summary>Спільні для сторінки й схеми клавіатури тексти статусів і кольори груп.</summary>
internal static class HotkeyPresenter
{
    // Середньої яскравості, щоб білий напис читався і на світлій, і на темній темі.
    private static readonly Color LauncherColor = Color.FromRgb(0x6C, 0x4F, 0xD8);

    private static readonly Color[] ModuleColors =
    [
        Color.FromRgb(0x1F, 0x7F, 0x8E),
        Color.FromRgb(0xB5, 0x5E, 0x24),
        Color.FromRgb(0x2F, 0x86, 0x4F),
        Color.FromRgb(0xB0, 0x3F, 0x73),
        Color.FromRgb(0x3A, 0x6F, 0xBF),
        Color.FromRgb(0x8A, 0x7A, 0x1E),
        Color.FromRgb(0x5B, 0x6E, 0x7C),
        Color.FromRgb(0xA8, 0x45, 0x45),
    ];

    /// <summary>Назва групи: «Лаунчер» або назва модуля.</summary>
    public static string ModuleTitle(RegisteredAction action) =>
        action.ModuleId == KastomSdk.LauncherModuleId ? HotkeyStrings.LauncherSection : action.ModuleName;

    /// <summary>Колір групи: лаунчер завжди фіолетовий, модулі — за стійким хешем id.</summary>
    public static Color GroupColor(string moduleId)
    {
        if (moduleId == KastomSdk.LauncherModuleId) return LauncherColor;

        uint hash = 2166136261;
        foreach (var ch in moduleId) hash = (hash ^ ch) * 16777619;
        return ModuleColors[hash % ModuleColors.Length];
    }

    public static string StatusText(HotkeyBindingInfo info, IReadOnlyList<HotkeyBindingInfo> all)
    {
        switch (info.Status)
        {
            case HotkeyBindingStatus.Active:
                return HotkeyStrings.StatusActive;
            case HotkeyBindingStatus.ConflictInternal:
                var owner = all.FirstOrDefault(b => b.Gesture == info.Gesture && b.Status == HotkeyBindingStatus.Active);
                return owner is null
                    ? HotkeyStrings.StatusConflictUnknown
                    : string.Format(CultureInfo.CurrentCulture, HotkeyStrings.StatusConflict, owner.Action.Action.Title);
            case HotkeyBindingStatus.TakenBySystem:
                return HotkeyStrings.StatusTakenBySystem;
            case HotkeyBindingStatus.Disabled:
                return HotkeyStrings.StatusDisabled;
            default:
                // Типове поєднання є, але його вже тримає інша дія.
                if (info.Action.Action.DefaultHotkey is { } wanted
                    && all.FirstOrDefault(b => b.Action.GlobalId != info.Action.GlobalId && b.Gesture == wanted) is { } holder)
                {
                    return string.Format(CultureInfo.CurrentCulture, HotkeyStrings.StatusDefaultTaken, holder.Action.Action.Title);
                }

                return HotkeyStrings.StatusUnassigned;
        }
    }
}
