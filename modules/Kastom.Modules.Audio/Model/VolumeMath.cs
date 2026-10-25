namespace Kastom.Modules.Audio.Model;

/// <summary>Перетворення гучності 0..1 у відсотки й кроки клавіш.</summary>
public static class VolumeMath
{
    /// <summary>0,456 → 46; так само округлює повзунок Windows.</summary>
    public static int ToPercent(float volume) =>
        (int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100, MidpointRounding.AwayFromZero);

    public static float FromPercent(double percent) => (float)(Math.Clamp(percent, 0, 100) / 100.0);

    /// <summary>
    /// Наступне значення для клавіш «гучніше» й «тихіше»: найближче кратне кроку в потрібний бік.
    /// З кроком 2 %: 43 % → 44 % угору, 43 % → 42 % униз, 44 % → 46 % угору.
    /// </summary>
    public static float Step(float volume, int stepPercent, bool up)
    {
        var step = Math.Clamp(stepPercent, 1, 100);
        var percent = ToPercent(volume);
        var next = up
            ? (percent / step + 1) * step
            : (percent % step == 0 ? percent - step : percent / step * step);
        return FromPercent(Math.Clamp(next, 0, 100));
    }
}
