using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitals.Cli;

/// <summary>Shared JSON settings so CLI output and tests agree on the wire format.</summary>
public static class JsonOutput
{
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    public static JsonSerializerOptions Pretty { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter() },
    };
}
