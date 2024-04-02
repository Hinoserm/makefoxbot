using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IStableDiffusionModel
{
    public string Title { get; }
    public string ModelName { get; }
    public string Hash { get; }
    public string SHA256 { get; }
    public string FileName { get; }
    public string Config { get; }
}

internal class StableDiffusionModelResponse
    : IStableDiffusionModel
{
    [JsonPropertyName("title")] public string Title { get; init; } = null!;
    [JsonPropertyName("model_name")] public string ModelName { get; init; } = null!;
    [JsonPropertyName("hash")] public string Hash { get; init; } = null!;
    [JsonPropertyName("sha256")] public string SHA256 { get; init; } = null!;
    [JsonPropertyName("filename")] public string FileName { get; init; } = null!;
    [JsonPropertyName("config")] public string Config { get; init; } = null!;
}