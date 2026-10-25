using Kastom.Core.Models;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Services;

namespace Kastom.Core.Hotkeys;

/// <summary>
/// Переносить повні поєднання з launcher.json до 0.1.3 ("audio/mute-mic" → "Ctrl+Alt+M") у нову схему:
/// адреса розділу (найчастіші модифікатори його збережених поєднань) і клавіша дії (збережений VK).
/// </summary>
public static class LegacyHotkeyMigration
{
    /// <summary>Повертає true, якщо щось перенесено й стан треба записати. Порожній старий словник не чіпає.</summary>
    public static bool Run(LauncherState state, IModuleLogger log)
    {
        if (state.Hotkeys.Count == 0) return false;

        var votes = new Dictionary<string, List<HotkeyModifiers>>(StringComparer.Ordinal);
        foreach (var (actionId, text) in state.Hotkeys)
        {
            var slash = actionId.IndexOf('/');
            if (slash <= 0) continue;
            var section = actionId[..slash];

            if (text.Length == 0)
            {
                // Користувач навмисно прибрав поєднання.
                state.HotkeyKeys.TryAdd(actionId, "");
                continue;
            }

            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                log.Warn(HotkeyMessages.Format("LogUnusableStored", text, actionId));
                continue;
            }

            // Нова схема (раптом поруч лежать обидва формати) важливіша за старий запис.
            state.HotkeyKeys.TryAdd(actionId, KeyNames.TryGetName(gesture.VirtualKey, out var name) ? name : "");
            if (!votes.TryGetValue(section, out var sets)) votes[section] = sets = [];
            sets.Add(gesture.Modifiers);
        }

        foreach (var (section, sets) in votes)
        {
            if (state.HotkeyAddresses.ContainsKey(section)) continue;
            if (HotkeyAddress.MostCommon(sets) is { } address) state.HotkeyAddresses[section] = HotkeyAddress.Format(address);
        }

        state.Hotkeys.Clear();
        return true;
    }
}
