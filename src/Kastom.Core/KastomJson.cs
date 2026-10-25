using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kastom.Core;

/// <summary>Спільні налаштування JSON: camelCase, переліки рядками, відступи.</summary>
public static class KastomJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
