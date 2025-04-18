using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface ISampler
{
    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyDictionary<string, string> Options { get; }
}

internal class SamplerResponse : ISampler
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("aliases")]
    public string[] Aliases { get; init; } = null!;

    [JsonPropertyName("options")]
    [JsonConverter(typeof(DictionaryStringStringConverter))]
    public Dictionary<string, string> Options { get; init; } = null!;

    IReadOnlyList<string> ISampler.Aliases => Aliases;
    IReadOnlyDictionary<string, string> ISampler.Options => Options;

    private class DictionaryStringStringConverter : JsonConverter<Dictionary<string, string>>
    {
        public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return dict;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                string key = reader.GetString()!;
                reader.Read();

                string value = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString()!,
                    JsonTokenType.Number => reader.TryGetInt64(out var i)
                        ? i.ToString(CultureInfo.InvariantCulture)
                        : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                    JsonTokenType.True => "true",
                    JsonTokenType.False => "false",
                    JsonTokenType.Null => "null",
                    _ => JsonSerializer.Serialize(JsonDocument.ParseValue(ref reader).RootElement)
                };

                dict[key] = value;
            }

            throw new JsonException("Unexpected end of JSON.");
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in value)
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }
            writer.WriteEndObject();
        }
    }
}
