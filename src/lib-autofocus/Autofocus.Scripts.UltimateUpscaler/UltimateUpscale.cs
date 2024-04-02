using Autofocus.Models;

namespace Autofocus.Scripts.UltimateUpscaler;

public record UltimateUpscale
    : IScriptConfig
{
    public IUpscaler? Upscaler { get; set; }

    public int TileWidth { get; set; } = 512;
    public int TileHeight { get; set; } = 512;

    public int Padding { get; set; } = 32;
    public int MaskBlur { get; set; } = 8;

    public RedrawMode RedrawMode { get; set; } = RedrawMode.Chess;

    public SeamsFixType SeamsFixType { get; set; } = SeamsFixType.None;
    public int SeamsFixMaskBlur { get; set; } = 8;
    public int SeamsFixPadding { get; set; } = 32;
    public double SeamsFixDenoise { get; set; } = 0.35;
    public int SeamsFixWidth { get; set; } = 64;

    public string Key => "ultimate sd upscale";

    public object ToJsonArgs()
    {
        return new object?[]
        {
            null, // not used!

            TileWidth,
            TileHeight,

            MaskBlur,
            Padding,
            SeamsFixWidth,
            SeamsFixDenoise,
            SeamsFixPadding,
            Upscaler?.Index,

            false, // save_upscaled_image a.k.a Upscaled

            (int)RedrawMode,

            false, // save_seams_fix_image a.k.a Seams fix

            SeamsFixMaskBlur,
            (int)SeamsFixType,

            // Ultimate upscaler allows overriding the img2img size settings. We're not going to expose
            // that, instead the size to upscale to will be defined by the img2img settings.
            0, // target_size_type. 0 == always get the target size from the img2img settings
            null, // custom_width (not used)
            null, // custom_height (not used)
            2 // custom_scale ???
        };
    }
}

public enum SeamsFixType
{
    None,
    BandPass,
    HalfTileOffset,
    HalfTileOffsetWithIntersections
}

public enum RedrawMode
{
    Linear,
    Chess,
    None
}