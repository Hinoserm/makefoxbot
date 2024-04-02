using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IPromptStyle
{
    public string Name { get; }
    public string Prompt { get; }
    public string NegativePrompt { get; }
}

internal class PromptStyleResponse
    : IPromptStyle
{
    [JsonPropertyName("name")] public string Name { get; init; } = null!;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = null!;
    [JsonPropertyName("negative_prompt")] public string NegativePrompt { get; init; } = null!;
}
