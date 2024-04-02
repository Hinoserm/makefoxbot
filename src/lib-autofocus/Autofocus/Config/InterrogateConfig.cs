using System.Text.Json.Serialization;

namespace Autofocus.Config;

public enum InterrogateModel
{
    CLIP,
    DeepDanbooru
}

public class InterrogateConfig
{
    public required Base64EncodedImage Image { get; init; }
    public InterrogateModel Model { get; set; } = InterrogateModel.CLIP;

    public bool? KeepModelsInMemory { get; set; }

    //todo:interrogate extras
    //"interrogate_return_ranks": false,
    //"interrogate_clip_num_beams": 1,
    //"interrogate_clip_min_length": 24,
    //"interrogate_clip_max_length": 48,
    //"interrogate_clip_dict_limit": 1500,
}

internal class InterrogateConfigRequest
{
    [JsonPropertyName("image")]
    public string Image { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; }

    [JsonPropertyName("override_settings")]
    public Dictionary<string, object> OverrideSettings = new();

    public InterrogateConfigRequest(InterrogateConfig config)
    {
        Image = config.Image.Base64();
        Model = config.Model.ToString().ToLowerInvariant();

        if (config.KeepModelsInMemory.HasValue)
            OverrideSettings["interrogate_keep_models_in_memory"] = config.KeepModelsInMemory.Value;
    }
}