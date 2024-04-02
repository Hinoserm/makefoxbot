using Autofocus.CtrlNet;

namespace Autofocus.Extensions.AfterDetailer;

public record Step
{
    public required string Model { get; set; }

    public string? PositivePrompt { get; set; }
    public string? NegativePrompt { get; set; }

    public bool RestoreFaces { get; set; }

    public double? DenoisingStrength { get; set; }

    public double? Confidence { get; set; }
    public double? MaskMinRatio { get; set; }
    public double? MaskMaxRatio { get; set; }

    public ControlNetModel? ControlNetModel { get; set; }
    public double? ControlNetWeight { get; set; }
    public double? ControlnetGuidanceStart { get; set; }
    public double? ControlnetGuidanceEnd { get; set; }

    public int? SamplerSteps { get; set; }
    public double? CfgScale { get; set; }

    public (int, int)? InpaintSize { get; set; }

    internal Dictionary<string, object?> ToJsonObject()
    {
        var args = new Dictionary<string, object?>
        {
            { "ad_model", Model },

            { "ad_restore_face", RestoreFaces }

            //"ad_dilate_erode": 32,
            //"ad_x_offset": 0,
            //"ad_y_offset": 0,
            //"ad_mask_merge_invert": "None",
            //"ad_mask_blur": 4,
            //"ad_inpaint_only_masked": true,
            //"ad_inpaint_only_masked_padding": 0,
        };

        if (PositivePrompt != null)
            args["ad_prompt"] = PositivePrompt;
        if (NegativePrompt != null)
            args["ad_negative_prompt"] = NegativePrompt;

        if (DenoisingStrength.HasValue)
            args["ad_denoising_strength"] = DenoisingStrength.Value;

        if (Confidence.HasValue)
            args["ad_confidence"] = Confidence.Value;

        if (SamplerSteps.HasValue)
        {
            args["ad_use_steps"] = true;
            args["ad_steps"] = SamplerSteps.Value;
        }

        if (CfgScale.HasValue)
        {
            args["ad_use_cfg_scale"] = true;
            args["ad_cfg_scale"] = CfgScale.Value;
        }

        if (MaskMinRatio.HasValue)
            args["ad_mask_min_ratio"] = MaskMinRatio.Value;
        if (MaskMaxRatio.HasValue)
            args["ad_mask_max_ratio"] = MaskMaxRatio.Value;

        if (InpaintSize.HasValue)
        {
            args["ad_use_inpaint_width_height"] = true;
            args["ad_inpaint_width"] = InpaintSize.Value.Item1;
            args["ad_inpaint_height"] = InpaintSize.Value.Item2;
        }

        if (ControlNetModel != null)
        {
            args["ad_controlnet_model"] = ControlNetModel.Name;

            if (ControlNetWeight.HasValue)
                args["ad_controlnet_weight"] = ControlNetWeight.Value;
            if (ControlnetGuidanceStart.HasValue)
                args["ad_controlnet_guidance_start"] = ControlnetGuidanceStart.Value;
            if (ControlnetGuidanceEnd.HasValue)
                args["ad_controlnet_guidance_end"] = ControlnetGuidanceEnd.Value;
        }

        return args;
    }
}