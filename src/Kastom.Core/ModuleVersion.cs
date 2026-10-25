using System.Diagnostics.CodeAnalysis;

namespace Kastom.Core;

/// <summary>
/// Версія у форматі SemVer: "1.2.3" або "1.2.3-beta.1". Метадані збірки після "+" відкидаються.
/// Передрелізна версія молодша за таку саму без суфікса: 1.0.0-beta &lt; 1.0.0.
/// </summary>
public sealed record ModuleVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<ModuleVersion>
{
    public static ModuleVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Неправильна версія: '{text}'.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out ModuleVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var core = text.Trim();
        var plus = core.IndexOf('+');
        if (plus >= 0) core = core[..plus];

        string? prerelease = null;
        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = core[(dash + 1)..];
            core = core[..dash];
            if (prerelease.Length == 0) return false;
        }

        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        if (!TryPart(parts[0], out var major) || !TryPart(parts[1], out var minor)) return false;
        var patch = 0;
        if (parts.Length == 3 && !TryPart(parts[2], out patch)) return false;

        version = new ModuleVersion(major, minor, patch, prerelease);
        return true;

        static bool TryPart(string s, out int value) =>
            int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    public int CompareTo(ModuleVersion? other)
    {
        if (other is null) return 1;
        var byNumbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumbers != 0) return byNumbers;
        if (Prerelease == other.Prerelease) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = int.TryParse(left[i], out var leftNumber);
            var rightIsNumber = int.TryParse(right[i], out var rightNumber);
            var result = (leftIsNumber, rightIsNumber) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };
            if (result != 0) return result;
        }

        return left.Length.CompareTo(right.Length);
    }

    public static bool operator <(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
