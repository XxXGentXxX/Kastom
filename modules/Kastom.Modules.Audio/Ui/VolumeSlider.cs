using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Повзунок гучності 0–100. Значення від користувача (перетягування, клік, клавіші, коліщатко) йдуть у
/// <see cref="VolumeRequested"/> не частіше ніж раз на 30 мс, остання точка — завжди. Значення з бекенда
/// ставить <see cref="SetVolume"/> і не чіпає повзунок, поки користувач ним керує.
/// </summary>
internal sealed class VolumeSlider : Slider
{
    private static readonly long ThrottleTicks = Stopwatch.Frequency * 30 / 1000;


    private readonly Func<int> _stepPercent;
    private readonly DispatcherTimer _timer;
    private bool _programmatic;
    private bool _dragging;
    private bool _pending;
    private bool _everSent;
    private long _lastSent;
    private int _inFlight;

    public VolumeSlider(Func<int> stepPercent)
    {
        _stepPercent = stepPercent;
        Minimum = 0;
        Maximum = 100;
        SmallChange = 1;
        LargeChange = 10;
        IsMoveToPointEnabled = true;

        // Неявний стиль Fluent задано для Slider і на нащадків він не діє: беремо його з ресурсів явно.
        SetResourceReference(StyleProperty, typeof(Slider));
        VerticalAlignment = System.Windows.VerticalAlignment.Center;

        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Flush();

        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _dragging = false;
            Flush();
        }));
        ValueChanged += (_, _) => OnValueChanged();
    }

    /// <summary>Відправити гучність (0..1) бекендові; задача завершується, коли команду виконано.</summary>
    public Func<float, Task>? VolumeRequested { get; set; }

    /// <summary>Остання команда повзунка виконана й нових немає: час перемалювати його за знімком бекенда.</summary>
    public Action? Settled { get; set; }

    /// <summary>Годинник для 30-мілісекундного обмеження (тіки Stopwatch); тести підміняють його, щоб не залежати від швидкості ПК.</summary>
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Користувач керує повзунком або його команда ще не виконана: значення з бекенда зараз ігноруємо.</summary>
    public bool IsBusy => _dragging || _pending || _timer.IsEnabled || _inFlight > 0;

    public void SetVolume(float volume)
    {
        if (IsBusy) return;
        var percent = VolumeMath.ToPercent(volume);
        if (Value == percent) return;

        _programmatic = true;
        try
        {
            Value = percent;
        }
        finally
        {
            _programmatic = false;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Delta != 0 && IsEnabled)
        {
            var next = VolumeMath.Step(VolumeMath.FromPercent(Value), _stepPercent(), e.Delta > 0);
            Value = VolumeMath.ToPercent(next);
        }

        // Лише над повзунком: решта сторінки гортається як зазвичай.
        e.Handled = true;
    }

    private void OnValueChanged()
    {
        if (_programmatic) return;

        _pending = true;
        if (_timer.IsEnabled) return;

        var elapsed = Clock() - _lastSent;
        if (!_everSent || elapsed >= ThrottleTicks)
        {
            Flush();
            return;
        }

        var wait = (ThrottleTicks - elapsed) * 1000 / Stopwatch.Frequency;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, wait));
        _timer.Start();
    }

    private void Flush()
    {
        _timer.Stop();
        if (!_pending) return;

        _pending = false;
        _everSent = true;
        _lastSent = Clock();
        _ = SendAsync(VolumeMath.FromPercent(Value));
    }

    private async Task SendAsync(float volume)
    {
        var request = VolumeRequested;
        if (request is null) return;

        _inFlight++;
        try
        {
            await request(volume);
        }
        catch
        {
            // Помилку вже показав власник у VolumeRequested; тут важливо лише зняти позначку «у дорозі».
        }
        finally
        {
            // Поки команда була в дорозі, значення з бекенда ігнорувалися: тепер їх треба підхопити.
            if (--_inFlight == 0) Settled?.Invoke();
        }
    }
}
