using Autofocus.Config;
using Autofocus.CtrlNet;
using Autofocus.ImageSharp.Extensions;
using Autofocus.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Autofocus.Terminal.TiledUpscaler;

public class TiledUpscaler
{
    private readonly StableDiffusion _api;
    private readonly IStableDiffusionModel _model;
    private readonly ISampler _sampler;

    private readonly int _steps;
    private readonly int _overlap;
    private readonly int _overlapHalf;

    public TiledUpscaler(StableDiffusion api, IStableDiffusionModel model, ISampler sampler, int steps = 40, int overlap = 64)
    {
        _api = api;
        _model = model;
        _sampler = sampler;
        _steps = steps;

        _overlapHalf = overlap / 2;
        _overlap = _overlapHalf * 2;
    }

    public async Task<Image> Upscale(Image source, PromptConfig prompt, int width, int height)
    {
        var cnet = await _api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");
        var cnetModel = await cnet.Model("control_v11f1e_sd15_tile");

        if (source.Width == width && source.Height == height)
            return source;

        // Clone input before mutating it!
        using var input = source.CloneAs<Rgb24>();

        // Scale up to the target size
        input.Mutate(a => a.Resize(new Size(width, height)));
        await input.SaveAsPngAsync("input_up.png");

        var output = new Image<Rgb24>(width, height);

        // Determine tile count, given a max tile size 512 - overlap
        var maxsize = 512 - _overlap;
        var tcx =  (int)Math.Ceiling(width / (float)maxsize);
        var tcy = (int)Math.Ceiling(height / (float)maxsize);
        var tw = width / tcx;
        var th = height / tcy;

        // Work through tiles one by one
        for (var x = 0; x < tcx; x++)
        {
            for (var y = 0; y < tcy; y++)
            {
                Console.WriteLine($"{x} {y}");

                var pos = new Point(x * tw, y * th);
                var (rect, postcrop) = AdjustBorders(new Rectangle(x * tw, y * th, tw, th), input.Size);

                var tile = input.CloneAs<Rgb24>();
                tile.Mutate(a => a.Crop(rect));
                await tile.SaveAsPngAsync($"tile_{x}_{y}_i.png");

                using var tileMask = new Image<Rgb24>(tile.Width, tile.Height);
                tileMask.Mutate(ctx =>
                {
                    ctx.Fill(Color.White)
                       .Fill(Color.Black, postcrop)
                       .BoxBlur(_overlapHalf / 2)
                       .Fill(Color.Black, postcrop);
                });

                var tileresult = await _api.Image2Image(new()
                {
                    Images = { await tile.ToAutofocusImageAsync() },
                    DenoisingStrength = 0.25,
                    //Mask = await tileMask.ToAutofocusImageAsync(),

                    Model = _model,
                    Sampler = new SamplerConfig
                    {
                        Sampler = _sampler,
                        SamplingSteps = _steps
                    },
                    Seed = new SeedConfig { Seed = 1234 },
                    Prompt = prompt,

                    Batches = 1,
                    BatchSize = 1,

                    Width = (uint)rect.Width,
                    Height = (uint)rect.Height,

                    AdditionalScripts =
                    {
                        new ControlNetConfig
                        {
                            Model = cnetModel,
                            Image = null,
                        },
                    }
                });

                using var tileimg = await tileresult.Images[0].ToImageSharpAsync();
                {
                    tileimg.Mutate(a => a.Crop(postcrop));

                    // ReSharper disable once AccessToDisposedClosure
                    output.Mutate(a => a.DrawImage(tileimg, pos, PixelColorBlendingMode.Normal, 1));
                }
            }
        }

        return output;
    }

    private (Rectangle, Rectangle) AdjustBorders(Rectangle rect, Size max)
    {
        var postcrop = new Rectangle(0, 0, rect.Width, rect.Height);

        if (rect.X > _overlapHalf)
        {
            rect.X -= _overlapHalf;
            rect.Width += _overlapHalf;
            postcrop.X = _overlapHalf;
        }

        if (rect.X + rect.Width + _overlapHalf < max.Width)
        {
            rect.Width += _overlapHalf;
        }

        if (rect.Y > _overlapHalf)
        {
            rect.Y -= _overlapHalf;
            rect.Height += _overlapHalf;
            postcrop.Y = _overlapHalf;
        }

        if (rect.Y + rect.Height + _overlapHalf < max.Height)
        {
            rect.Height += _overlapHalf;
        }

        return (rect, postcrop);
    }
}