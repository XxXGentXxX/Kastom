using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace Kastom.Launcher.Settings;

/// <summary>
/// Перемикач, який UI Automation (екранний диктор, Voice Access) перемикає так само, як клік мишею.
/// Звичайний ToggleButton через UI Automation змінює IsChecked без події Click, тож обробники на Click мовчали
/// (знайдено на встановленій збірці 2026-10-25). Програмне IsChecked = … обробники, як і раніше, не викликає.
/// </summary>
public sealed class SwitchToggle : ToggleButton
{
    /// <summary>Те саме, що клік: перемкнути й підняти Click.</summary>
    public void PerformClick() => OnClick();

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchTogglePeer(this);

    private sealed class SwitchTogglePeer(SwitchToggle owner) : ToggleButtonAutomationPeer(owner), IToggleProvider
    {
        ToggleState IToggleProvider.ToggleState => owner.IsChecked switch
        {
            true => ToggleState.On,
            false => ToggleState.Off,
            _ => ToggleState.Indeterminate,
        };

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.PerformClick();
        }
    }
}
