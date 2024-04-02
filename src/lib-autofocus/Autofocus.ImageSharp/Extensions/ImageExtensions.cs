using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace Autofocus.ImageSharp.Extensions;

public static class ImageExtensions
{
    public static Base64EncodedImage ToAutofocusImage(this Image image)
    {
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return new Base64EncodedImage(stream.ToArray());
    }

    public static async Task<Base64EncodedImage> ToAutofocusImageAsync(this Image image)
    {
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        return new Base64EncodedImage(stream.ToArray());
    }

    public static Rgba32 AverageColor(this Image image)
    {
        using var averageImg = image.CloneAs<Rgba32>();
        averageImg.Mutate(ctx => ctx.Quantize(new OctreeQuantizer(new QuantizerOptions
        {
            Dither = null,
            MaxColors = 1,
        })));
        return averageImg[0, 0];
    }
}