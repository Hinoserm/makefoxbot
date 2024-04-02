using System.Text.Json.Serialization;
using Autofocus.Models;
using Autofocus.Scripts;

namespace Autofocus.Config;

public record ImageToImageConfig
{
    public List<Base64EncodedImage> Images { get; } = new();

    public required PromptConfig Prompt { get; set; }
    public required SeedConfig Seed { get; set; }
    public required SamplerConfig Sampler { get; set; }
    public required IStableDiffusionModel Model { get; set; }

    public double? DenoisingStrength { get; set; }
    public uint? Width { get; set; }
    public uint? Height { get; set; }
    public bool Tiling { get; set; }

    public int? BatchSize { get; set; }
    public int? Batches { get; set; }

    public bool RestoreFaces { get; set; }

    public uint? ClipSkip { get; init; }

    public Base64EncodedImage? Mask { get; set; }
    public int MaskBlur { get; set; }
    public MaskFillMode? InpaintingFill { get; set; }

    public bool InpaintingMaskInvert { get; set; }

    public List<IAdditionalScriptConfig?> AdditionalScripts { get; set; } = new();
    public IScriptConfig? Script { get; set; }

    public string? TaskId { get; set; }

    /*
     * {
  "resize_mode": 0,
  "image_cfg_scale": 0,
  "inpaint_full_res": true,
  "inpaint_full_res_padding": 0,
  "initial_noise_multiplier": 0,
  "do_not_save_samples": false,
  "do_not_save_grid": false,
  "s_min_uncond": 0,
  "s_churn": 0,
  "s_tmax": 0,
  "s_tmin": 0,
  "s_noise": 1,
  "include_init_images": false,
  "send_images": true,
  "save_images": false,
}
     */
}

public enum MaskFillMode
{
    /// <summary>
    /// Fill with colours of the images
    /// </summary>
    Fill = 0,

    /// <summary>
    /// Fill with original content
    /// </summary>
    original = 1,
    
    /// <summary>
    /// Fill with latent noise
    /// </summary>
    LatentNoise = 2,
    
    /// <summary>
    /// Fill with latent space zeros
    /// </summary>
    LatentNothing = 3,
}

internal class ImageToImageConfigRequest
{
    [JsonPropertyName("init_images")]
    public string[] Images { get; init; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; }

    [JsonPropertyName("negative_prompt")]
    public string NegativePrompt { get; init; }

    [JsonPropertyName("styles")]
    public string[] Styles { get; init; }

    [JsonPropertyName("width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Width { get; init; }

    [JsonPropertyName("height"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Height { get; init; }

    [JsonPropertyName("tiling"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Tiling { get; init; }

    [JsonPropertyName("seed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }

    [JsonPropertyName("subseed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SubSeed { get; init; }

    [JsonPropertyName("subseed_strength"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? SubSeedStrength { get; init; }

    [JsonPropertyName("seed_resize_from_w"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? SeedResizeFromWidth { get; init; }

    [JsonPropertyName("seed_resize_from_h"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? SeedResizeFromHeight { get; init; }

    [JsonPropertyName("n_iter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Batches { get; init; }

    [JsonPropertyName("batch_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BatcheSize { get; init; }

    [JsonPropertyName("restore_faces")]
    public bool RestoreFaces { get; init; }

    [JsonPropertyName("steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SamplingSteps { get; init; }

    [JsonPropertyName("sampler_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SamplerName { get; init; }

    [JsonPropertyName("cfg_scale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? CfgScale { get; init; }

    [JsonPropertyName("eta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Eta { get; init; }

    [JsonPropertyName("denoising_strength"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? DenoisingStrength { get; init; }

    [JsonPropertyName("mask"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Base64EncodedImage? Mask { get; init; }

    [JsonPropertyName("mask_blur")]
    public int MaskBlur { get; init; }

    [JsonPropertyName("inpainting_mask_invert"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InpaintingMaskInvert { get; init; }

    [JsonPropertyName("inpainting_fill"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InpaintingFill { get; init; }

    [JsonPropertyName("include_init_images")]
    public bool IncludeInitImages { get; set; }

    [JsonPropertyName("force_task_id")]
    public string? ForceTaskId { get; set; }


    [JsonPropertyName("script_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScriptName { get; init; }

    [JsonPropertyName("script_args"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ScriptArgs { get; init; }


    [JsonPropertyName("override_settings")]
    public Dictionary<string, object> OverrideSettings = new();

    [JsonPropertyName("override_settings_restore_afterwards")]
    public bool RestoreAfterOverrides { get; init; }

    [JsonPropertyName("alwayson_scripts")]
    public Dictionary<string, object> AlwaysOnScripts { get; set; } = new();


    public ImageToImageConfigRequest(ImageToImageConfig config)
    {
        Images = config.Images.Select(a => a.Base64()).ToArray();
        Prompt = config.Prompt.Positive;
        NegativePrompt = config.Prompt.Negative;
        Styles = config.Prompt.Styles.Select(a => a.Name).ToArray();
        Seed = config.Seed.Seed;
        SubSeed = config.Seed.SubSeed;
        SubSeedStrength = (float?)config.Seed.SubseedStrength;
        SeedResizeFromWidth = config.Seed.SeedResizeFromWidth;
        SeedResizeFromHeight = config.Seed.SeedResizeFromHeight;
        Width = config.Width;
        Height = config.Height;
        Tiling = config.Tiling;
        BatcheSize = config.BatchSize;
        Batches = config.Batches;
        RestoreFaces = config.RestoreFaces;
        SamplingSteps = config.Sampler.SamplingSteps;
        CfgScale = (float?)config.Sampler.CfgScale;
        SamplerName = config.Sampler.Sampler.Name;
        Eta = (float?)config.Sampler.Eta;
        DenoisingStrength = (float?)config.DenoisingStrength;
        Mask = config.Mask;
        MaskBlur = config.MaskBlur;
        InpaintingFill = (int?)config.InpaintingFill;
        InpaintingMaskInvert = config.InpaintingMaskInvert;
        IncludeInitImages = false;
        ForceTaskId = config.TaskId;

        if (config.Script != null)
        {
            ScriptName = config.Script.Key;
            ScriptArgs = config.Script.ToJsonArgs();
        }

        RestoreAfterOverrides = true;
        OverrideSettings.Add("sd_model_checkpoint", config.Model.Title);

        if (config.ClipSkip.HasValue)
            OverrideSettings.Add("CLIP_stop_at_last_layers", config.ClipSkip.Value);

        foreach (var item in config.AdditionalScripts)
            if (item != null)
                AlwaysOnScripts.Add(item.Key, item.ToJsonObject());
    }
}