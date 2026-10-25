using System.Windows.Threading;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Спільне для всіх елементів одного мікшера: хост, режим, останній знімок, запуск команд.</summary>
internal sealed class MixerContext(MixerHost host, MixerMode mode, Dispatcher dispatcher, Action refresh)
{
    public MixerHost Host { get; } = host;

    public MixerMode Mode { get; } = mode;

    public bool IsFlyout => Mode == MixerMode.Flyout;

    /// <summary>Знімок, який зараз намальовано.</summary>
    public AudioSnapshot Snapshot { get; set; } = AudioSnapshot.Empty;

    public IAudioBackend Backend => Host.Backend;

    public AudioOptions Options => Host.Options;

    /// <summary>Перемалювати мікшер за останнім знімком бекенда.</summary>
    public void Refresh() => refresh();

    /// <summary>
    /// Виконати команду бекенда. Помилка не виходить назовні: вона йде в <c>Shell.ReportError</c> з текстом
    /// <paramref name="errorMessage"/>. Після будь-якого результату мікшер перемальовується за знімком бекенда,
    /// тож повзунок, якому відмовили, повертається на справжнє значення. Повертає, чи вдалася команда.
    /// </summary>
    public async Task<bool> Run(Func<Task> command, string errorMessage)
    {
        var succeeded = true;
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            succeeded = false;
            Report(errorMessage, ex);
        }

        refresh();
        return succeeded;
    }

    /// <summary>Повідомити про помилку, яка не з команди бекенда (наприклад, запис налаштування).</summary>
    public void Report(string message, Exception exception)
    {
        void Send()
        {
            try
            {
                Host.Shell.ReportError(message, exception);
            }
            catch
            {
                // Повідомлення про помилку саме не має ламати обробник події.
            }
        }

        if (dispatcher.CheckAccess()) Send();
        else dispatcher.BeginInvoke(Send);
    }
}
