using System.Windows.Controls;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Де показано мікшер.</summary>
public enum MixerMode
{
    /// <summary>Власна сторінка модуля в лаунчері (широка, усе розгорнуто).</summary>
    Page,

    /// <summary>Панель біля значка в області сповіщень (вузька).</summary>
    Flyout,

    /// <summary>Віджет: лише загальна гучність і мікрофон.</summary>
    Widget,
}

/// <summary>Мікшер (З1–З10). ЗАГЛУШКА елемента E0: справжню реалізацію пише елемент E2.</summary>
public sealed class MixerView : UserControl
{
    public MixerView(MixerHost host, MixerMode mode)
    {
        Host = host;
        Mode = mode;
    }

    public MixerHost Host { get; }

    public MixerMode Mode { get; }
}
