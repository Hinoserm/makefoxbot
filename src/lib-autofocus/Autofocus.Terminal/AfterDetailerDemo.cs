using Autofocus.Config;
using Autofocus.Extensions.AfterDetailer;
using Autofocus.ImageSharp.Extensions;
using SixLabors.ImageSharp;

namespace Autofocus.Terminal;

public class AfterDetailerDemo
{
    public async Task Run()
    {
        var api = new StableDiffusion();
        await api.Ping();

        var config = new TextToImageConfig
        {
            Seed = 44,

            Prompt = new()
            {
                Positive = "1girl, sylvanas, elf, evil, red eyes, banshee queen, ice power, full body",
                Negative = "easynegative, badhandv4, bad-hands-5, logo, Watermark, username, signature, jpeg artifacts,, (nsfw:1.4), (spider:1.4)",
            },

            Sampler = new()
            {
                Sampler = await api.Sampler("UniPC"),
                SamplingSteps = 20,
            },

            Model = await api.StableDiffusionModel("cardosAnime_v20"),
            BatchSize = 1,
            Batches = 1,
            RestoreFaces = false,
            Height = 512,
            Width = 512,
        };

        var txt2img = await api.TextToImage(config);
        for (var i = 0; i < txt2img.Images.Count; i++)
            await (await txt2img.Images[i].ToImageSharpAsync()).SaveAsPngAsync($"txt2img_image{i}.png");

        config.AdditionalScripts.Add(new AfterDetailer
        {
            Steps =
            {
                new()
                {
                    Model = "face_yolov8s.pt",
                    PositivePrompt = "detailed, masterpiece, angry, green eyes, elf ears"
                }
            }
        });

        var txt2img2 = await api.TextToImage(config);
        for (var i = 0; i < txt2img2.Images.Count; i++)
            await (await txt2img2.Images[i].ToImageSharpAsync()).SaveAsPngAsync($"txt2img2_image{i}.png");
    }
}