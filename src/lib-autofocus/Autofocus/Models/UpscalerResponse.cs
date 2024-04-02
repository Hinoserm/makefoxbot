using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IUpscaler
{
    public string Name { get; }
    public string ModelName { get; }
    public string ModelPath { get; }
    public string ModelUrl { get; }
    public double Scale { get; }
    public int Index { get; }
}

internal class UpscalerResponse
    : IUpscaler
{
    [JsonPropertyName("name")] public string Name { get; init; } = null!;
    [JsonPropertyName("model_name")] public string ModelName { get; init; } = null!;
    [JsonPropertyName("model_path")] public string ModelPath { get; init; } = null!;
    [JsonPropertyName("model_url")] public string ModelUrl { get; init; } = null!;
    [JsonPropertyName("scale")] public double Scale { get; init; }

    public int Index { get; set; }
}