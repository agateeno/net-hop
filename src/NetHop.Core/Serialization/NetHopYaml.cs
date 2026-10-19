using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetHop.Core.Serialization;

/// <summary>Raised when a YAML document cannot be read; carries the position of the problem.</summary>
public sealed class NetHopFormatException(string message, int line, int column, Exception? inner = null)
    : Exception(line > 0 ? $"Line {line}, column {column}: {message}" : message, inner)
{
    public int Line { get; } = line;

    public int Column { get; } = column;
}

/// <summary>Shared YAML settings: camelCase keys and enum values, defaults omitted on write.</summary>
internal static class NetHopYaml
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(
            DefaultValuesHandling.OmitNull |
            DefaultValuesHandling.OmitDefaults |
            DefaultValuesHandling.OmitEmptyCollections)
        .DisableAliases()
        .Build();

    // Unknown keys are errors on purpose: a typo like "adress" must not silently do nothing.
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string Serialize<T>(T value) => Serializer.Serialize(value);

    public static T Deserialize<T>(string yaml) where T : new()
    {
        try
        {
            return Deserializer.Deserialize<T>(yaml) ?? new T();
        }
        catch (YamlException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            throw new NetHopFormatException(message, (int)ex.Start.Line, (int)ex.Start.Column, ex);
        }
    }
}
