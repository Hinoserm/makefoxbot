using System.Text.Json.Serialization;

namespace Autofocus.Models;

internal class PngInfoRequest
{
    [JsonPropertyName("image")] public Base64EncodedImage Image { get; init; }

    public PngInfoRequest(Base64EncodedImage image)
    {
        Image = image;
    }
}

public interface IPngInfo
{
    public string Info { get; }
    public string Parameters { get; }
    public (float, float) DPI { get; }
}

internal class PngInfoResponse
    : IPngInfo
{
    [JsonPropertyName("info")] public string Info { get; init; } = null!;
    [JsonPropertyName("items")] public PngInfoItemsResponse Items { get; init; } = null!;

    string IPngInfo.Parameters => Items.Parameters;
    (float, float) IPngInfo.DPI => (Items.DPI[0], Items.DPI[1]);
}

internal class PngInfoItemsResponse
{
    [JsonPropertyName("parameters")] public string Parameters { get; init; } = null!;
    [JsonPropertyName("dpi")] public float[] DPI { get; init; } = null!;
}