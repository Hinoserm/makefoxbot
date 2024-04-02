using Autofocus.CtrlNet;
using Autofocus.ImageSharp.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Autofocus.Terminal;

public class VideoPrototyping
{
    public async Task Run()
    {
        var api = new StableDiffusion();
        var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

        var model = await api.StableDiffusionModel("cardosAnime_v20");
        //var cnetModelDepth = await cnet.Model("control_v11f1p_sd15_depth [cfd03158]");
        var cnetModelNorm = await cnet.Model("control_v11p_sd15_normalbae [316696f1]");
        //var cnetInvert = await cnet.Module("invert");
        //var cnetThreshold = await cnet.Module("threshold");
        var sampler = await api.Sampler("DPM++ SDE");

        var frames = Directory.EnumerateFiles("./../../../../frames_norm").ToArray();
        Directory.CreateDirectory("frames_output");
        Directory.CreateDirectory("masks_output");

        Base64EncodedImage? prevFrame = null;
        var frameNum = 0;
        foreach (var frame in frames)
        {
            var normImage = await Image.LoadAsync(frame);
            var normImg = await normImage.ToAutofocusImageAsync();

            var normMask = normImage.CloneAs<Rgb24>();
            normMask.Mutate(ctx =>
            {
                ctx.BinaryThreshold(0.01f)
                   .Invert();
            });
            var maskImg = await normMask.ToAutofocusImageAsync();
            await normMask.SaveAsPngAsync($"masks_output/mask_img_{frameNum}.png");

            //// Invert depth image
            //var depthImg = await cnet.Preprocess(new ControlNetPreprocessConfig
            //{
            //    Module = cnetInvert,
            //    Images = { Image.Load(frame).ToEncodedImage() },
            //});
            //depthImg.Images[0].ToImage().Save($"masks_output/depth_img_{frameNum}.png");

            //// Extract a mask
            //var maskImg = await cnet.Preprocess(new ControlNetPreprocessConfig
            //{
            //    Module = cnetThreshold,
            //    ParameterA = 240,
            //    Images = { Image.Load(frame).ToEncodedImage() },
            //});

            //// Invert Mask
            //var invert = await cnet.Preprocess(new ControlNetPreprocessConfig
            //{
            //    Module = cnetInvert,
            //    Images = { maskImg },
            //});
            //invert.Images[0].ToImage().Save($"masks_output/mask_img_{frameNum}.png");
            //maskImg = invert.Images[0];

            // Generate 1st frame using txt2img, generate new frames using img2img from that
            if (frameNum == 0)
            {
                var txt2img = await api.TextToImage(
                    new()
                    {
                        Seed = 22,

                        Prompt = new()
                        {
                            Positive = "1girl, backpack, outdoors, mountains, sunny, frilled_skirt, glasses, short_hair, short_sleeves, skirt, smile, solo, standing, thighhighs",
                            Negative = "easynegative, badhandv4, nsfw",
                        },

                        Sampler = new()
                        {
                            Sampler = sampler,
                            SamplingSteps = 15,
                        },

                        Model = model,
                        BatchSize = 1,
                        Batches = 1,
                        RestoreFaces = false,
                        Height = 512,
                        Width = 512,

                        AdditionalScripts = {
                    new ControlNetConfig {
                        Image = normImg,
                        Model = cnetModelNorm,
                        Mask = maskImg
                    }
                        }
                    }
                );

                prevFrame = txt2img.Images[0];
                await (await prevFrame.ToImageSharpAsync()).SaveAsPngAsync($"frames_output/{frameNum++}.png");
            }
            else
            {
                var img2img = await api.Image2Image(
                    new()
                    {
                        Seed = 22,
                        Images = { prevFrame! },

                        Prompt = new()
                        {
                            Positive = "1girl, backpack, outdoors, mountains, sunny, frilled_skirt, glasses, short_hair, short_sleeves, skirt, smile, solo, standing, thighhighs",
                            Negative = "easynegative, badhandv4, nsfw",
                        },

                        Sampler = new()
                        {
                            Sampler = sampler,
                            SamplingSteps = 10,
                        },

                        Model = model,
                        BatchSize = 1,
                        Batches = 1,
                        RestoreFaces = false,
                        Height = 512,
                        Width = 512,

                        AdditionalScripts =
                        {
                    new ControlNetConfig
                    {
                        Image = normImg,
                        Model = cnetModelNorm,
                        Mask = maskImg,
                        GuidanceStart = 0,
                        GuidanceEnd = 0.75,
                        ControlMode = ControlMode.Balance,
                    }
                        }
                    }
                );

                prevFrame = img2img.Images[0];
                await (await prevFrame.ToImageSharpAsync()).SaveAsPngAsync($"frames_output/{frameNum++}.png");
            }
        }
    }
}