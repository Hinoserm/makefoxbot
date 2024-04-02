using SixLabors.ImageSharp;

namespace Autofocus.ImageSharp.Extensions;

public static class Base64EncodedImageExtensions
{
    public static Image ToImageSharp(this Base64EncodedImage image)
    {
        var stream = new MemoryStream();
        stream.Write(image.Data.Span);
        stream.Position = 0;
        return Image.Load(stream);
    }

    public static Task<Image> ToImageSharpAsync(this Base64EncodedImage image)
    {
        var stream = new MemoryStream();
        stream.Write(image.Data.Span);
        stream.Position = 0;
        return Image.LoadAsync(stream);
    }
}