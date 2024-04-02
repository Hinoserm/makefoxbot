using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

internal class ControlNetVersionResponse
{
    [JsonPropertyName("version")] public int Version { get; set; }
}