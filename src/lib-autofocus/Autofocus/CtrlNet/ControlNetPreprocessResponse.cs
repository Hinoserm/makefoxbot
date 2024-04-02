using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

public interface IControlNetPreprocess
{
    public List<Base64EncodedImage> Images { get; }
}

internal class ControlNetPreprocessResponse
    : IControlNetPreprocess
{
    [JsonPropertyName("images")] public List<Base64EncodedImage> Images { get; set; } = null!;
}