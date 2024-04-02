using Autofocus.Config;
using Autofocus.ImageSharp.Extensions;
using Autofocus.Outpaint;
using SixLabors.ImageSharp;

namespace Autofocus.Terminal;

public class Outpaint2Demo
{
    const int FRAME_COUNT = 1;
    const bool CREATE_GIF = false;

    public async Task Run()
    {
        var api = new StableDiffusion();
        await api.Ping();

        var genPrompt = new PromptConfig
        {
            Positive = "that thing I left in that place that one time",
            //Positive = "1girl, white hair, purple eyes, black choker, slight smile, mountains, clouds, sky",
            Negative = "easynegative, badhandv4",
        };

        var model = await api.StableDiffusionModel("cardosAnime_v20");
        var config = new TextToImageConfig
        {
            Seed = new(),

            Prompt = genPrompt,

            Sampler = new()
            {
                Sampler = await api.Sampler("UniPC"),
                SamplingSteps = 20,
            },

            Model = model,
            BatchSize = 1,
            Batches = 1,
            RestoreFaces = false,
            Height = 512,
            Width = 512,
        };

        if (!File.Exists("txt2img_image.png"))
        {
            // Generate initial image
            var txt2img = await api.TextToImage(config);
            await (await txt2img.Images.Single().ToImageSharpAsync()).SaveAsPngAsync("txt2img_image.png");
        }
        var startFrame = await Image.LoadAsync("txt2img_image.png");

        // outpainting prompt
        var outpaintPrompt = genPrompt;

        // Outpaint
        var outpainter = new TwoStepOutpainter(api, model, await api.Sampler("DDIM"))
        {
            UseControlNetTile = false,
        };
        var results = await outpainter.Outpaint(outpaintPrompt, startFrame, Console.WriteLine);
        for (var i = 0; i < results.Count; i++)
            await (await results[i].ToImageSharpAsync()).SaveAsPngAsync($"outpaint_{i}.png");
    }
}