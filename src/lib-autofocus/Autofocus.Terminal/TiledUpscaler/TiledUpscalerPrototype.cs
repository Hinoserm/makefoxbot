using Autofocus.Config;
using Autofocus.ImageSharp.Extensions;
using SixLabors.ImageSharp;

namespace Autofocus.Terminal.TiledUpscaler;

public class TiledUpscalerPrototype
{
    public async Task Run()
    {
        var api = new StableDiffusion();
        var sampler = await api.Sampler("DDIM");
        var model = await api.StableDiffusionModel("cardosAnime_v20");

        var prompt = new PromptConfig
        {
            Positive = "1girl, backpack, outdoors, mountains, sunny, frilled_skirt, glasses, looking_at_viewer, short_hair, short_sleeves, skirt, smile, solo, standing, thighhighs",
            Negative = "easynegative, badhandv4, nsfw",
        };

        if (!File.Exists("Input.png"))
        {
            var txt2img = await api.TextToImage(
                new()
                {
                    Seed = 25,

                    Prompt = prompt,

                    Sampler = new()
                    {
                        Sampler = sampler,
                        SamplingSteps = 20,
                    },

                    Model = model,
                    BatchSize = 1,
                    Batches = 1,
                    RestoreFaces = false,
                    Height = 512,
                    Width = 512,
                }
            );

            var img = txt2img.Images[0];
            await (await img.ToImageSharpAsync()).SaveAsPngAsync("Input.png");
        }



        var input = await Image.LoadAsync("Input.png");
        var upscaler = new TiledUpscaler(api, model, sampler);
        var result = await upscaler.Upscale(input, prompt, 2000, 2000);
        await result.SaveAsPngAsync("output.png");
    }
}