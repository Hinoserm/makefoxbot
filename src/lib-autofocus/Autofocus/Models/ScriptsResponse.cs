using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IScriptsResponse
{
    IReadOnlyList<string> Txt2Img { get; }
    IReadOnlyList<string> Img2Img { get; }
}

internal class ScriptsResponse
    : IScriptsResponse
{
    [JsonPropertyName("txt2img")] public string[] Txt2Img { get; init; } = null!;
    [JsonPropertyName("img2img")] public string[] Img2Img { get; init; } = null!;

    IReadOnlyList<string> IScriptsResponse.Txt2Img => Txt2Img;
    IReadOnlyList<string> IScriptsResponse.Img2Img => Img2Img;
}