using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SkiaSharp;
using Svg.Skia;
using static System.Net.Mime.MediaTypeNames;

using Image = SixLabors.ImageSharp.Image;

namespace makefoxsrv
{
    internal class FoxWatermark
    {
        private static readonly Image<Rgba32>? DarkWatermark = LoadSvgAsImage("../data/watermarks/dark.svg");
        private static readonly Image<Rgba32>? LightWatermark = LoadSvgAsImage("../data/watermarks/light.svg");

        public static Image<Rgba32> ApplyWatermark(Image<Rgba32> inputImage)
        {
            try
            {
                if (DarkWatermark == null || LightWatermark == null)
                {
                    return inputImage; // Return the original image if watermarks are not loaded
                }

                var image = inputImage.Clone();

                // Define the expected size for the watermark
                var watermarkSize = new Size(64, 64);

                // Define possible corners
                var corners = new[]
                {
                    new Point(0, 0),
                    new Point(image.Width - watermarkSize.Width, 0),
                    new Point(0, image.Height - watermarkSize.Height),
                    new Point(image.Width - watermarkSize.Width, image.Height - watermarkSize.Height)
                };

                // Adding a slight bias to bottom corners
                var biases = new[] { 1f, 1f, 1.1f, 1.1f };

                int bestCornerIndex = 0;
                float bestContrast = float.NegativeInfinity;
                Point bestPosition = default;

                // Evaluate each corner with random offsets
                Random random = new Random();
                for (int i = 0; i < corners.Length; i++)
                {
                    var offsetX = random.Next(10, 51); // Random offset between 10 and 50 pixels
                    var offsetY = random.Next(10, 51); // Random offset between 10 and 50 pixels

                    var corner = corners[i];
                    var position = new Point(
                        corner.X + (corner.X == 0 ? offsetX : -offsetX),
                        corner.Y + (corner.Y == 0 ? offsetY : -offsetY)
                    );

                    var contrast = GetCornerContrast(image, watermarkSize, position) * biases[i];

                    if (contrast > bestContrast)
                    {
                        bestCornerIndex = i;
                        bestContrast = contrast;
                        bestPosition = position;
                    }
                }

                var bestCorner = corners[bestCornerIndex];
                var darkPixels = 0;
                var lightPixels = 0;

                // Count pixels in the selected corner
                for (int y = bestPosition.Y; y < bestPosition.Y + watermarkSize.Height; y++)
                {
                    for (int x = bestPosition.X; x < bestPosition.X + watermarkSize.Width; x++)
                    {
                        if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
                        {
                            var color = image[x, y];
                            float brightness = (color.R * 0.299f + color.G * 0.587f + color.B * 0.114f) / 255f;
                            if (brightness > 0.5f)
                            {
                                lightPixels++;
                            }
                            else
                            {
                                darkPixels++;
                            }
                        }
                    }
                }

                // Choose watermark based on pixel counts
                using var watermark = darkPixels > lightPixels ? LightWatermark.Clone() : DarkWatermark.Clone();

                // Resize the watermark to ensure it does not exceed 64x64 pixels
                if (watermark.Width > watermarkSize.Width || watermark.Height > watermarkSize.Height)
                {
                    watermark.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = watermarkSize,
                        Mode = ResizeMode.Max
                    }));
                }

                // Apply opacity to the watermark
                //watermark.Mutate(ctx => ctx.Opacity(0.3f));

                // Draw the watermark on the image
                image.Mutate(ctx => ctx.DrawImage(watermark, new Point(bestPosition.X, bestPosition.Y), 1f));

                return image;
            }
            catch (Exception ex)
            {
                FoxLog.Write($"Failed to apply watermark to image.  Error: {ex.Message}\r\n{ex.StackTrace}");
                return inputImage; // Return the original image if an error occurs
            }
        }

        private static Image<Rgba32>? LoadSvgAsImage(string svgPath)
        {
            try
            {
                var svg = new SKSvg();
                svg.Load(svgPath);
                var svgPicture = svg.Picture;

                int width = (int)svgPicture.CullRect.Width;
                int height = (int)svgPicture.CullRect.Height;

                using (var bitmap = new SKBitmap(width, height))
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawPicture(svgPicture);
                    using (var img = SKImage.FromBitmap(bitmap))
                    using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        using (var ms = new MemoryStream())
                        {
                            data.SaveTo(ms);
                            ms.Seek(0, SeekOrigin.Begin);
                            var image = Image.Load<Rgba32>(ms);

                            // Apply opacity to the watermark
                            image.Mutate(ctx => ctx.Opacity(0.3f));

                            image.Mutate(ctx => ctx.Resize(new ResizeOptions
                            {
                                Size = new Size(64, 64),
                                Mode = ResizeMode.Max
                            }));

                            return image;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to load SVG watermark {svgPath}: {ex.Message}\r\n{ex.StackTrace}");
                return null;
            }
        }

        private static float GetCornerContrast(Image<Rgba32> image, Size watermarkSize, Point position)
        {
            int lightPixels = 0;
            int darkPixels = 0;

            for (int y = position.Y; y < position.Y + watermarkSize.Height; y++)
            {
                for (int x = position.X; x < position.X + watermarkSize.Width; x++)
                {
                    if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
                    {
                        var color = image[x, y];
                        float brightness = (color.R * 0.299f + color.G * 0.587f + color.B * 0.114f) / 255f;
                        if (brightness > 0.5f)
                        {
                            lightPixels++;
                        }
                        else
                        {
                            darkPixels++;
                        }
                    }
                }
            }

            return darkPixels - lightPixels; // Higher value means more suitable for a light watermark
        }

        private static Point GetCornerPosition(string corner, Size imageSize, Size watermarkSize)
        {
            Random random = new Random();
            int offsetX = random.Next(10, 51); // Random offset between 10 and 50 pixels
            int offsetY = random.Next(10, 51); // Random offset between 10 and 50 pixels

            switch (corner)
            {
                case "topLeft":
                    return new Point(offsetX, offsetY);
                case "topRight":
                    return new Point(imageSize.Width - watermarkSize.Width - offsetX, offsetY);
                case "bottomLeft":
                    return new Point(offsetX, imageSize.Height - watermarkSize.Height - offsetY);
                case "bottomRight":
                    return new Point(imageSize.Width - watermarkSize.Width - offsetX, imageSize.Height - watermarkSize.Height - offsetY);
                default:
                    throw new ArgumentException("Invalid corner specified.");
            }
        }
    }
}
