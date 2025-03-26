using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Autofocus.Models;
using Autofocus.Scripts;

namespace Autofocus.Config;

public record TextToImageConfig
{
    public required PromptConfig Prompt { get; set; }
    public required SeedConfig Seed { get; set; }
    public required SamplerConfig Sampler { get; set; }
    public required SchedulerConfig Scheduler { get; set; }

    public required IStableDiffusionModel Model { get; set; }

    public required uint Width { get; set; }
    public required uint Height { get; set; }
    public bool Tiling { get; set; }

    public int? BatchSize { get; set; }
    public int? Batches { get; set; }

    public bool RestoreFaces { get; set; }

    public uint? ClipSkip { get; init; }

    public HighResConfig? HighRes { get; set; }

    public List<IAdditionalScriptConfig?> AdditionalScripts { get; set; } = new();
    public IScriptConfig? Script { get; set; }

    public string? TaskId { get; set; }

    //"firstphase_width": 0,
    //"firstphase_height": 0,
    //"hr_second_pass_steps": 0,
    //"s_min_uncond": 0,
    //"s_churn": 0,
    //"s_tmax": 0,
    //"s_tmin": 0,
    //"s_noise": 1,
    //"script_args": [],
    //"script_name": "string",
    //"send_images": true,
    //"save_images": false,
    //"do_not_save_samples": false,
    //"do_not_save_grid": false,
}

public record HighResConfig
{
    public required double DenoisingStrength { get; set; }

    public required IUpscaler Upscaler { get; set; }

    public required uint Width { get; set; }
    public required uint Height { get; set; }
    public uint Steps { get; set; } = 0;
}

internal class TextToImageConfigRequest
{
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

    [JsonPropertyName("steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SamplingSteps { get; init; }

    [JsonPropertyName("sampler_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SamplerName { get; init; }

    [JsonPropertyName("scheduler"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchedulerName { get; init; }

    [JsonPropertyName("cfg_scale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? CfgScale { get; init; }

    [JsonPropertyName("eta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Eta { get; init; }

    [JsonPropertyName("n_iter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Batches { get; init; }

    [JsonPropertyName("batch_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BatcheSize { get; init; }

    [JsonPropertyName("restore_faces")]
    public bool RestoreFaces { get; init; }

    [JsonPropertyName("force_task_id")]
    public string? ForceTaskId { get; set; }


    [JsonPropertyName("enable_hr")]
    public bool EnableHr { get; init; }

    [JsonPropertyName("denoising_strength")]
    public float DenoisingStrength { get; init; }

    [JsonPropertyName("hr_upscaler"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Upscaler { get; init; }

    [JsonPropertyName("hr_resize_x"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? UpscaleWidth { get; init; }

    [JsonPropertyName("hr_resize_y"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? UpscaleHeight { get; init; }

    [JsonPropertyName("hr_second_pass_steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? UpscaleSteps { get; init; }


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


    public TextToImageConfigRequest(TextToImageConfig config)
    {
        Prompt = config.Prompt.Positive;
        NegativePrompt = config.Prompt.Negative;
        Styles = config.Prompt.Styles.Select(a => a.Name).ToArray();
        Height = config.Height;
        Width = config.Width;
        Tiling = config.Tiling;
        Seed = config.Seed.Seed;
        SubSeed = config.Seed.SubSeed;
        SubSeedStrength = (float?)config.Seed.SubseedStrength;
        SeedResizeFromWidth = config.Seed.SeedResizeFromWidth;
        SeedResizeFromHeight = config.Seed.SeedResizeFromHeight;
        SamplingSteps = config.Sampler.SamplingSteps;
        SamplerName = config.Sampler.Sampler.Name;
        SchedulerName = config.Scheduler.Scheduler.Name;
        CfgScale = (float?)config.Sampler.CfgScale;
        Eta = (float?)config.Sampler.Eta;
        Batches = config.Batches;
        BatcheSize = config.BatchSize;
        RestoreFaces = config.RestoreFaces;
        ForceTaskId = config.TaskId;
        EnableHr = config.HighRes != null;
        if (config.HighRes != null)
        {
            DenoisingStrength = (float)config.HighRes.DenoisingStrength;
            Upscaler = config.HighRes.Upscaler.Name;
            UpscaleWidth = config.HighRes.Width;
            UpscaleHeight = config.HighRes.Height;
            UpscaleSteps = config.HighRes.Steps;
        }

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