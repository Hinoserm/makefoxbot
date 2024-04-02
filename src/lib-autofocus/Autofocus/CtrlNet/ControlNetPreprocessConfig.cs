using System.Text.Json.Serialization;

namespace Autofocus.CtrlNet;

public class ControlNetPreprocessConfig
{
    public required ControlNetModule Module { get; init; }

    public List<Base64EncodedImage> Images { get; set; } = new();

    public int? Resolution { get; set; }

    public float? ParameterA { get; set; }
    public float? ParameterB { get; set; }
}

internal class ControlNetPreprocessConfigModel
{
    [JsonPropertyName("controlnet_module")]
    public string ControlNetModule { get; set; }

    [JsonPropertyName("controlnet_input_images")]
    public string[] InputImages { get; set; }

    [JsonPropertyName("controlnet_processor_res"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Resolution { get; set; }

    [JsonPropertyName("controlnet_threshold_a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ParameterA { get; set; }

    [JsonPropertyName("controlnet_threshold_b"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ParameterB { get; set; }

    public ControlNetPreprocessConfigModel(ControlNetPreprocessConfig config)
    {
        Check(config.Module.Resolution, config.Resolution);

        if (config.Module.Parameters.Count > 0)
        {
            var a = config.Module.Parameters[0];
            Check(a, config.ParameterA);
        }

        if (config.Module.Parameters.Count > 1)
        {
            var b = config.Module.Parameters[1];
            Check(b, config.ParameterB);
        }

        ControlNetModule = config.Module.Name;
        InputImages = config.Images.Select(img => img.Base64()).ToArray();
        Resolution = config.Resolution;
        ParameterA = config.ParameterA;
        ParameterB = config.ParameterB;
    }

    private static void Check(ControlNetModule.Parameter? limit, float? value)
    {
        if (limit == null)
            return;
        if (value == null)
            return;

        if (value.Value < limit.Min)
            throw new ArgumentException($"Parameter {limit.Value} < {limit.Min} ({limit.Name}) is too small", nameof(value));
        if (value.Value > limit.Max)
            throw new ArgumentException($"Parameter {limit.Value} > {limit.Min} ({limit.Name}) is too large", nameof(value));
    }
}