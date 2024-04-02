using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface ITextToImageResult
{
    public IReadOnlyList<Base64EncodedImage> Images { get; }
    public ITextToImageResultParameters Parameters { get; }
}

public interface ITextToImageResultParameters
{
    public string Prompt { get; }
    public string NegativePrompt { get; }
}

internal class TextToImageResultResponse
    : ITextToImageResult
{
    [JsonPropertyName("images")] public Base64EncodedImage[] Images { get; init; } = null!;
    [JsonPropertyName("parameters")] public TextToImageResultParametersResponse Parameters { get; init; } = null!;

    IReadOnlyList<Base64EncodedImage> ITextToImageResult.Images => Images;
    ITextToImageResultParameters ITextToImageResult.Parameters => Parameters;
}

internal class TextToImageResultParametersResponse
    : ITextToImageResultParameters
{
    //[JsonPropertyName("enable_hr")] public bool EnableHr { get; init; }
    //[JsonPropertyName("denoising_strength")] public double DenoisingStrength { get; init; }

    [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
    [JsonPropertyName("negative_prompt")] public string NegativePrompt { get; init; } = "";

    //"firstphase_width":0,"firstphase_height":0,"hr_scale":2.0,"hr_upscaler":null,"hr_second_pass_steps":0,"hr_resize_x":0,"hr_resize_y":0
    //,"styles":null,"seed":15,"subseed":-1,"subseed_strength":0,"seed_resize_from_h":-1,"seed_resize_from_w":-1,"sampler_name":null,"batch_size":1,"n_iter":1,"steps":50,"cfg_scale":7.0,"width":512,"height":512,"restore_faces":false,"tiling":false,"do_not_save_samples":false,"do_not_save_grid":false,"negative_prompt":null,"eta":null,"s_min_uncond":0.0,"s_churn":0.0,"s_tmax":null,"s_tmin":0.0,"s_noise":1.0,"override_settings":null,"override_settings_restore_afterwards":true,"script_args":[],"sampler_index":"Euler","script_name":null,"send_images":true,"save_images":false,"alwayson_scripts":{}},"info":"{\"prompt\": \"masterpiece, sharp, A starry sky\", \"all_prompts\": [\"masterpiece, sharp, A starry sky\"], \"negative_prompt\": \"\", \"all_negative_prompts\": [\"\"], \"seed\": 15, \"all_seeds\": [15], \"subseed\": 635562390, \"all_subseeds\": [635562390], \"subseed_strength\": 0, \"width\": 512, \"height\": 512, \"sampler_name\": \"Euler\", \"cfg_scale\": 7.0, \"steps\": 50, \"batch_size\": 1, \"restore_faces\": false, \"face_restoration_model\": null, \"sd_model_hash\": \"c0d1994c73\", \"seed_resize_from_w\": -1, \"seed_resize_from_h\": -1, \"denoising_strength\": 0, \"extra_generation_params\": {}, \"index_of_first_image\": 0, \"infotexts\": [\"masterpiece, sharp, A starry sky\\nSteps: 50, Sampler: Euler, CFG scale: 7.0, Seed: 15, Size: 512x512, Model hash: c0d1994c73, Model: realisticVisionV20_v20, Seed resize from: -1x-1, Denoising strength: 0, Version: v1.2.1\"], \"styles\": [], \"job_timestamp\": \"20230527223839\", \"clip_skip\": 1, \"is_using_inpainting_conditioning\": false}"}
}