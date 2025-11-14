using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClopWindows.Core.Settings;

internal static class SettingsSerialization
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };
}
