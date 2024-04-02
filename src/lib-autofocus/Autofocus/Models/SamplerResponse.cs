using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface ISampler
{
    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyDictionary<string, string> Options { get; }
}

internal class SamplerResponse
    : ISampler
{
    [JsonPropertyName("name")] public string Name { get; init; } = null!;
    [JsonPropertyName("aliases")] public string[] Aliases { get; init; } = null!;
    [JsonPropertyName("options")] public Dictionary<string, string> Options { get; init; } = null!;

    IReadOnlyList<string> ISampler.Aliases => Aliases;
    IReadOnlyDictionary<string, string> ISampler.Options => Options;
}