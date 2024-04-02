using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

internal class ControlNetModuleListResponse
{
    [JsonPropertyName("module_list")] public string[] ModuleList { get; set; } = null!;
    [JsonPropertyName("module_detail")] public Dictionary<string, ControlnetModuleDetailResponse> Details { get; set; } = null!;
}

internal class ControlnetModuleDetailResponse
{
    [JsonPropertyName("sliders")] public ControlnetModuleDetailSliderResponse[] Sliders { get; set; } = null!;
}

internal class ControlnetModuleDetailSliderResponse
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("value")] public float Value { get; set; }
    [JsonPropertyName("min")] public float Min { get; set; }
    [JsonPropertyName("max")] public float Max { get; set; }
}