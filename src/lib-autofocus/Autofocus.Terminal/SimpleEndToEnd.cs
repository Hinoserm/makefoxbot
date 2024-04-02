using Autofocus.Config;
using Autofocus.ImageSharp.Extensions;
using Autofocus.Scripts.UltimateUpscaler;
using SixLabors.ImageSharp;

namespace Autofocus.Terminal;

public class SimpleEndToEnd
{
    public async Task Run()
    {
        var api = new StableDiffusion();
        await api.Ping();

        var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");
        _ = await cnet.Model("control_v11e_sd15_ip2p");

        var model = await api.StableDiffusionModel("cardosAnime_v20");
        var sampler = await api.Sampler("DPM++ SDE");
        var upscaler = await api.Upscaler("R-ESRGAN 4x+ Anime6B");

        var txt2img = await api.TextToImage(
            new()
            {
                Seed = 22,

                Prompt = new()
                {
                    Positive = "1girl, backpack, outdoors, mountains, sunny, frilled_skirt, glasses, looking_at_viewer, short_hair, short_sleeves, skirt, smile, solo, standing, thighhighs",
                    Negative = "easynegative, badhandv4, nsfw",
                    Styles = {
                        await api.Style("BWphoto"),
                    }
                },

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

                AdditionalScripts =
                {
                    //new ControlNetConfig()
                    //{
                    //    Image = Image.Load("standing_06.png").ToEncodedImage(),
                    //    Model = await cnet.Model("control_v11p_sd15_openpose [cab727d4]")
                    //},
                }
            }
        );

        for (var i = 0; i < txt2img.Images.Count; i++)
        {
            await (await txt2img.Images[i].ToImageSharpAsync()).SaveAsPngAsync($"txt2img_image{i}.png");

            var interrogate = await api.Interrogate(new InterrogateConfig
            {
                Image = txt2img.Images[i],
                Model = InterrogateModel.DeepDanbooru
            });
            Console.WriteLine(interrogate.Caption);
        }

        Console.WriteLine("# PngInfo");
        var info = await api.PngInfo(txt2img.Images[0]);
        Console.WriteLine(info.Info);

        Console.WriteLine();
        Console.WriteLine("Starting Image2Image");

        var img2img = await api.Image2Image(
            new()
            {
                Images = {
                    txt2img.Images[0]
                },

                Model = model,

                Prompt = new()
                {
                    Positive = "1boy, (adult), backpack, outdoors, mountains, sunny, glasses, looking_at_viewer, short_hair, short_sleeves, smile, solo, standing, (standing_on_one:1.25)",
                    Negative = "easynegative, badhandv4, nsfw, child",
                    Styles =
                    {
                        await api.Style("TellTale")
                    }
                },

                Seed = new()
                {
                    Seed = 22,
                },

                Sampler = new()
                {
                    Sampler = sampler,
                    SamplingSteps = 20,
                },
            }
        );

        for (var i = 0; i < img2img.Images.Count; i++)
            await (await img2img.Images[i].ToImageSharpAsync()).SaveAsPngAsync($"img2img_image{i}.png");

        Console.WriteLine();
        Console.WriteLine("Starting Image2Image Ultimate Upscaler");

        var upscale = await api.Image2Image(
            new()
            {
                Images = {
                    img2img.Images[0]
                },

                Model = model,

                Prompt = new()
                {
                    Positive = "1boy, (adult), backpack, outdoors, mountains, sunny, glasses, looking_at_viewer, short_hair, short_sleeves, smile, solo, standing, (standing_on_one:1.25)",
                    Negative = "easynegative, badhandv4, nsfw, child",
                    Styles =
                    {
                        await api.Style("TellTale")
                    }
                },

                Seed = new()
                {
                    Seed = 22,
                },

                Sampler = new()
                {
                    Sampler = sampler,
                    SamplingSteps = 20,
                },

                Width = 1024,
                Height = 1024,
                DenoisingStrength = 0.22,
                Script = new UltimateUpscale
                {
                    Upscaler = upscaler
                }
            }
        );

        for (var i = 0; i < img2img.Images.Count; i++)
            await (await upscale.Images[i].ToImageSharpAsync()).SaveAsPngAsync($"upscale_image{i}.png");
    }
}