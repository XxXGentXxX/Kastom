using System.Text.RegularExpressions;

namespace Kastom.Core.Runtime;

internal static partial class IdPattern
{
    /// <summary>Латиниця в нижньому регістрі, цифри, дефіси: "show-greeting".</summary>
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
