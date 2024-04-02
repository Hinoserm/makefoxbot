using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

internal class ControlNetModelListResponse
{
    [JsonPropertyName("model_list")] public string[] ModelList { get; set; } = null!;
}