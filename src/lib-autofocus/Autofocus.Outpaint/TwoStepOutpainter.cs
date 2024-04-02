using Autofocus.Config;
using Autofocus.CtrlNet;
using Autofocus.ImageSharp.Extensions;
using Autofocus.Models;
using Autofocus.Outpaint.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Autofocus.Outpaint;

/// <summary>
/// Given an image paint out a 128 pixel border all the way around
/// This works in two main steps:<br />
/// 1. Downscale the image. Paint the original content into the middle. Randomly "bleed" pixels of the content out
///    into the border (giving some noisy context). Run image2image with high steps and a mask proecting original content.<br />
/// 2. Scale back up to the correct size, run image2image with low steps and no mask. This cleans up blurriness and allows it to adjust the original content slightly.
/// </summary>
public class TwoStepOutpainter
{
    private readonly IStableDiffusion _api;
    private readonly IStableDiffusionModel _model;
    private readonly ISampler _sampler;

    private readonly GraphicsOptions _drawingOptions;

    public int BatchSize1 { get; set; } = 2;
    public int BatchSize2 { get; set; } = 2;

    public int Steps { get; set; } = 75;
    private int Steps1 => (int)Math.Ceiling(Steps * 0.65);
    private int Steps2 => (int)Math.Ceiling(Steps * 0.35);

    public bool UseControlNetTile { get; set; } = true;

    public TwoStepOutpainter(IStableDiffusion api, IStableDiffusionModel model, ISampler sampler)
    {
        _api = api;
        _model = model;
        _sampler = sampler;

        _drawingOptions = new GraphicsOptions
        {
            Antialias = true,
            ColorBlendingMode = PixelColorBlendingMode.Normal,
        };
    }

    public Task<IReadOnlyList<Base64EncodedImage>> Outpaint(PromptConfig prompt, Base64EncodedImage input, Action<float> progressCallback)
    {
        return Outpaint(prompt, input, report =>
        {
            progressCallback(report.Progress);
            return Task.CompletedTask;
        });
    }

    public async Task<IReadOnlyList<Base64EncodedImage>> Outpaint(PromptConfig prompt, Base64EncodedImage input, Func<ProgressReport, Task> progressCallback)
    {
        using var inputImage = await input.ToImageSharpAsync();
        return await Outpaint(prompt, inputImage, progressCallback);
    }

    public Task<IReadOnlyList<Base64EncodedImage>> Outpaint(PromptConfig prompt, Image input, Action<float> progressCallback)
    {
        return Outpaint(prompt, input, report =>
        {
            progressCallback(report.Progress);
            return Task.CompletedTask;
        });
    }

    public async Task<IReadOnlyList<Base64EncodedImage>> Outpaint(PromptConfig prompt, Image input, Func<ProgressReport, Task> progressCallback)
    {
        var progress = new ProgressMonitor(_api);
        progress.ProgressEvent += progressCallback;

        // Calculate average colour of the whole image
        await progress.Report(0.01f);

        // Create an image expanded outwards by 128 in all directions
        using var inputImage = new Image<Rgba32>(input.Width + 256, input.Height + 256);
        inputImage.Mutate(ctx =>
        {
            ctx.Fill(Color.Gray);
            ctx.DrawImage(input, new Point(128, 128), _drawingOptions);
        });

        // "Bleed" random pixels out from the image into the blank space
        inputImage.Bleed(new Rectangle(128, 128, input.Width - 1, input.Height - 1), 80, null);

        //return new[] { await inputImage.ToAutofocusImageAsync() };

        await progress.Report(0.05f);

        // Create a mask covering the noise with a smooth transition into the image
        using var inputMask = new Image<Rgba32>(inputImage.Width, inputImage.Height);
        inputMask.Mutate(ctx =>
        {
            const int blur = 8;
            var rect = new RectangleF(128 + blur, 128 + blur, input.Width - blur * 2, input.Height - blur * 2);

            ctx.Fill(Color.White)
               .Fill(Color.Black, rect)
               .BoxBlur(blur);
        });
        await progress.Report(0.1f);

        // Shrink inputs down to the size of the original input for the first step
        inputImage.Mutate(ctx => ctx.Resize(input.Size));
        inputMask.Mutate(ctx => ctx.Resize(input.Size));
        var inputImageAutofocus = await inputImage.ToAutofocusImageAsync();

        // Build the controlnet config if possible
        var cnetConfig = await GetControlNetConfig(inputImageAutofocus);

        // Run img2img over the entire image. The mask protects most of the original content from being changed at all.
        var result1 = await progress.Report(0.1f, 0.5f,
            _api.Image2Image(new ImageToImageConfig
            {
                Images =
                {
                    inputImageAutofocus
                },

                Mask = await inputMask.ToAutofocusImageAsync(),

                Model = _model,
                DenoisingStrength = 0.75,

                BatchSize = BatchSize1,

                Width = (uint)inputMask.Width,
                Height = (uint)inputMask.Height,

                Prompt = prompt,
                Seed = -1,

                ClipSkip = 2,

                Sampler = new()
                {
                    Sampler = _sampler,
                    SamplingSteps = Steps1,
                    CfgScale = 6
                },

                AdditionalScripts =
                {
                    cnetConfig
                }
            })
        );
        await progress.Report(0.5f);

        var progPerBatch = 0.5f / result1.Images.Count;
        var baseProgress = 0.5f;
        var results = new List<Base64EncodedImage>();
        foreach (var item in result1.Images)
        {
            await progress.Report(baseProgress);
            {
                using var image = await item.ToImageSharpAsync();
                image.Mutate(ctx =>
                {
                    // Scale the image back up to the full size
                    ctx.Resize(input.Size + new Size(256, 256));

                    // Draw the original content into the middle
                    const int margin = 24;
                    ctx.DrawImage(input, new Point(128, 128), new Rectangle(margin, margin, input.Width - margin, input.Height - margin), new GraphicsOptions
                    {
                        BlendPercentage = 0.85f
                    });
                });

                // Redraw the entire image at the full scale. This step has a very low number of steps and low denoising, so it shouldn't change the composition
                // of the overall image too much. But it should fix up the seams that we just made much worse by painting the original image back in!
                var inner = await progress.Report(baseProgress, baseProgress + progPerBatch, Redraw(image, prompt, -1));
                results.AddRange(inner);
            }
            baseProgress += progPerBatch;
            await progress.Report(baseProgress);
        }
        return results;
    }

    private async Task<IReadOnlyCollection<Base64EncodedImage>> Redraw(Image input, PromptConfig prompt, SeedConfig seed)
    {
        var inputImageAutofocus = await input.ToAutofocusImageAsync();

        var result2 = await _api.Image2Image(new ImageToImageConfig
        {
            Images =
            {
                inputImageAutofocus,
            },

            Model = _model,
            DenoisingStrength = 0.3,

            BatchSize = BatchSize2,

            Width = (uint)input.Width,
            Height = (uint)input.Height,

            Prompt = prompt,
            Seed = seed,

            Sampler = new()
            {
                Sampler = _sampler,
                SamplingSteps = Steps2,
                CfgScale = 8
            },
        });

        return result2.Images;
    }

    private async Task<ControlNetConfig?> GetControlNetConfig(Base64EncodedImage conditionImage)
    {
        if (!UseControlNetTile)
            return null;

        var cnet = await _api.TryGetControlNet();
        if (cnet == null)
            return null;
        
        var cnetModel = await cnet.Model("control_v11f1e_sd15_tile");
        if (cnetModel == null)
            return null;
        
        return new ControlNetConfig
        {
            Image = conditionImage,
            Model = cnetModel,
            Weight = 1,
            ControlMode = ControlMode.ControlNetImportant,
            GuidanceStart = 0,
            GuidanceEnd = 1,
        };
    }
}