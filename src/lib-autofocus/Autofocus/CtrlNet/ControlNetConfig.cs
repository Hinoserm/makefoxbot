using Autofocus.Config;
using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

public enum ResizeMode
{
    JustResize = 0,
    ScaleToFit = 1,
    Envelope = 2,
}

public enum ControlMode
{
    Balance = 0,
    PromptImportant = 1,
    ControlNetImportant = 2,
}

public record ControlNetConfig
    : IAdditionalScriptConfig
{
    public required Base64EncodedImage? Image { get; set; }
    public Base64EncodedImage? Mask { get; set; }

    public required ControlNetModel Model { get; set; }

    public double? Weight { get; set; }
    public double? GuidanceStart { get; set; }
    public double? GuidanceEnd { get; set; }

    public ResizeMode? ResizeMode { get; set; }
    public ControlMode? ControlMode { get; set; }

    public bool? LowVRam { get; set; }

    //Preprocessing _can_ be included here, but it's already supported as a separate step.
    //public required ControlNetModule Module { get; set; }
    //public int? PrecessorResolution { get; set; }
    //public float? PrecessorParameterA { get; set; }
    //public float? PrecessorParameterB { get; set; }
    //public bool? PixelPerfectPreprocessor { get; set; }

    string IAdditionalScriptConfig.Key => "controlnet";

    object IAdditionalScriptConfig.ToJsonObject()
    {
        return new
        {
            args = new[] { new ControlNetConfigModel(this) }
        };
    }
}

internal class ControlNetConfigModel
{
    [JsonPropertyName("input_image")]
    public Base64EncodedImage? Image { get; init; }

    [JsonPropertyName("mask"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Base64EncodedImage? Mask { get; init; }

    [JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ModelName { get; init; }

    [JsonPropertyName("weight"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Weight { get; set; }

    [JsonPropertyName("resize_mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResizeMode { get; set; }

    [JsonPropertyName("lowvram"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LowVRam { get; set; }

    [JsonPropertyName("guidance_start"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GuidanceStart { get; set; }

    [JsonPropertyName("guidance_end"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GuidanceEnd { get; set; }

    [JsonPropertyName("control_mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ControlMode { get; set; }

    public ControlNetConfigModel(ControlNetConfig config)
    {
        Image = config.Image;
        Mask = config.Mask;
        ModelName = config.Model.Name;
        Weight = config.Weight;
        ResizeMode = (int?)config.ResizeMode;
        LowVRam = config.LowVRam;
        GuidanceStart = config.GuidanceStart;
        GuidanceEnd = config.GuidanceEnd;
        ControlMode = (int?)config.ControlMode;
    }
}