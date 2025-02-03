using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Drawing.Processing;

namespace makefoxsrv
{
    internal static class FoxStickerify
    {
        /// <summary>
        /// Processes the sticker image in one pass.
        /// It flood‑fills from the corners removing pixels close to the background color
        /// or nearly white (within an extra margin), builds the subject mask, erodes it
        /// inward by a specified number of pixels, and then anti‑aliases the edge by
        /// averaging over a 3×3 neighborhood over the binary subject mask (forcing solid white).
        /// Optionally, a drop shadow is added.
        /// </summary>
        /// <param name="source">The source image.</param>
        /// <param name="tolerance">Color difference tolerance for background removal.</param>
        /// <param name="extraMargin">Extra pixels to remove when encountering near‑white areas.</param>
        /// <param name="inwardEdge">Number of pixels inward from the subject edge to anti‑alias.</param>
        /// <param name="addDropShadow">If true, adds a drop shadow beneath the sticker.</param>
        /// <param name="dropShadowOffsetX">Horizontal offset (in pixels) for the drop shadow.</param>
        /// <param name="dropShadowOffsetY">Vertical offset (in pixels) for the drop shadow.</param>
        /// <param name="dropShadowBlurSigma">Gaussian blur sigma for the drop shadow.</param>
        /// <param name="dropShadowOpacity">Drop shadow opacity (0–1).</param>
        /// <returns>The processed image, with an optional drop shadow.</returns>
        public static Image<Rgba32> ProcessSticker(
            Image<Rgba32> source,
            int tolerance = 100,
            int extraMargin = 3,
            int inwardEdge = 3,
            bool addDropShadow = false,
            int dropShadowOffsetX = 5,
            int dropShadowOffsetY = 5,
            float dropShadowBlurSigma = 3f,
            float dropShadowOpacity = 0.5f)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            int width = source.Width;
            int height = source.Height;
            // Clone the image so the original remains unchanged.
            Image<Rgba32> result = source.Clone();

            // Copy the pixels into a 2D array for random access.
            Rgba32[,] pixels = new Rgba32[width, height];
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        pixels[x, y] = row[x];
                    }
                }
            });

            // Compute the background color as the average of the four corners.
            Rgba32 c1 = pixels[0, 0];
            Rgba32 c2 = pixels[width - 1, 0];
            Rgba32 c3 = pixels[0, height - 1];
            Rgba32 c4 = pixels[width - 1, height - 1];
            byte avgR = (byte)((c1.R + c2.R + c3.R + c4.R) / 4);
            byte avgG = (byte)((c1.G + c2.G + c3.G + c4.G) / 4);
            byte avgB = (byte)((c1.B + c2.B + c3.B + c4.B) / 4);
            Rgba32 bgColor = new Rgba32(avgR, avgG, avgB, 255);

            // Flood fill from the corners.
            // For nearly white pixels, allow removal if within extraMargin.
            bool[,] visited = new bool[width, height];
            var queue = new Queue<(int x, int y, int depth)>();
            queue.Enqueue((0, 0, 0));
            queue.Enqueue((width - 1, 0, 0));
            queue.Enqueue((0, height - 1, 0));
            queue.Enqueue((width - 1, height - 1, 0));

            while (queue.Count > 0)
            {
                (int x, int y, int depth) = queue.Dequeue();
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                if (visited[x, y])
                    continue;
                visited[x, y] = true;

                Rgba32 pix = pixels[x, y];
                bool isNearlyWhite = (pix.R > 220 && pix.G > 220 && pix.B > 220);
                double diff = Math.Sqrt(
                    (pix.R - bgColor.R) * (pix.R - bgColor.R) +
                    (pix.G - bgColor.G) * (pix.G - bgColor.G) +
                    (pix.B - bgColor.B) * (pix.B - bgColor.B));
                if (diff < tolerance || (isNearlyWhite && depth <= extraMargin))
                {
                    pixels[x, y] = new Rgba32(255, 255, 255, 0);
                    // Enqueue the 4-connected neighbors.
                    queue.Enqueue((x + 1, y, depth + 1));
                    queue.Enqueue((x - 1, y, depth + 1));
                    queue.Enqueue((x, y + 1, depth + 1));
                    queue.Enqueue((x, y - 1, depth + 1));
                }
            }

            // Write the modified pixels back into the image.
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        row[x] = pixels[x, y];
                    }
                }
            });

            // Build the subject mask (non‑transparent pixels).
            bool[,] subjectMask = new bool[width, height];
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        subjectMask[x, y] = row[x].A > 0;
                    }
                }
            });

            // Erode the subject mask inward by 'inwardEdge' pixels.
            bool[,] erodedMask = Erode(subjectMask, inwardEdge);

            // For every pixel that is in the subject mask but not in the eroded mask,
            // compute an anti‑aliased alpha from a 3×3 neighborhood over the binary subject mask,
            // and force its color to solid white.
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        if (subjectMask[x, y] && !erodedMask[x, y])
                        {
                            int count = 0;
                            int sum = 0;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                    {
                                        count++;
                                        sum += subjectMask[nx, ny] ? 255 : 0;
                                    }
                                }
                            }
                            byte newAlpha = (byte)(sum / count);
                            row[x] = new Rgba32(255, 255, 255, newAlpha);
                        }
                    }
                }
            });

            // Optionally add a drop shadow.
            if (addDropShadow)
            {
                result = AddDropShadow(result, dropShadowOffsetX, dropShadowOffsetY, dropShadowBlurSigma, dropShadowOpacity);
            }

            return result;
        }

        /// <summary>
        /// Adds a drop shadow to the given sticker image.
        /// The drop shadow is created from the sticker’s alpha mask, filled with solid black (with the given opacity),
        /// blurred, and then composited behind the sticker on a larger canvas.
        /// </summary>
        /// <param name="sticker">The processed sticker image.</param>
        /// <param name="offsetX">Horizontal offset for the shadow.</param>
        /// <param name="offsetY">Vertical offset for the shadow.</param>
        /// <param name="blurSigma">Gaussian blur sigma to soften the shadow.</param>
        /// <param name="opacity">Shadow opacity (0–1).</param>
        /// <returns>A new image containing the drop shadow and the original sticker composited together.</returns>
        private static Image<Rgba32> AddDropShadow(
            Image<Rgba32> sticker,
            int offsetX,
            int offsetY,
            float blurSigma,
            float opacity)
        {
            int width = sticker.Width;
            int height = sticker.Height;
            // Compute extra space needed on each side.
            int extraLeft = offsetX < 0 ? -offsetX : 0;
            int extraTop = offsetY < 0 ? -offsetY : 0;
            int extraRight = offsetX > 0 ? offsetX : 0;
            int extraBottom = offsetY > 0 ? offsetY : 0;
            int canvasWidth = width + extraLeft + extraRight;
            int canvasHeight = height + extraTop + extraBottom;

            // Create a new canvas for the final composite.
            Image<Rgba32> canvas = new Image<Rgba32>(canvasWidth, canvasHeight);
            canvas.Mutate(ctx => ctx.Clear(new Rgba32(0, 0, 0, 0)));

            // Create the drop shadow layer from the sticker’s alpha mask.
            Image<Rgba32> dropShadow = new Image<Rgba32>(width, height);
            dropShadow.Mutate(ctx => ctx.Clear(new Rgba32(0, 0, 0, 0)));

            // First, extract the sticker's alpha channel into a temporary 2D array.
            byte[,] stickerAlpha = new byte[width, height];
            sticker.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        stickerAlpha[x, y] = row[x].A;
                    }
                }
            });

            // Fill the drop shadow layer based on the extracted alpha.
            dropShadow.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        row[x] = stickerAlpha[x, y] > 0
                            ? new Rgba32(0, 0, 0, (byte)(255 * opacity))
                            : new Rgba32(0, 0, 0, 0);
                    }
                }
            });

            // Blur the drop shadow.
            dropShadow.Mutate(ctx => ctx.GaussianBlur(blurSigma));

            // Composite the drop shadow onto the canvas.
            // Place the sticker at (extraLeft, extraTop) and the shadow offset accordingly.
            canvas.Mutate(ctx =>
            {
                ctx.DrawImage(
                    dropShadow,
                    new Point(extraLeft + offsetX, extraTop + offsetY),
                    1f);
                ctx.DrawImage(
                    sticker,
                    new Point(extraLeft, extraTop),
                    1f);
            });

            dropShadow.Dispose();
            return canvas;
        }

        /// <summary>
        /// Performs binary erosion on a mask using a square structuring element
        /// of size (2 * radius + 1) x (2 * radius + 1).
        /// A pixel in the output is true only if every pixel in its neighborhood is true.
        /// </summary>
        private static bool[,] Erode(bool[,] mask, int radius)
        {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);
            bool[,] output = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool allTrue = true;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height || !mask[nx, ny])
                            {
                                allTrue = false;
                                break;
                            }
                        }
                        if (!allTrue)
                            break;
                    }
                    output[x, y] = allTrue;
                }
            }
            return output;
        }

        public static Image<Rgba32> CropAndEnsure512x512(Image<Rgba32> input)
        {
            if (input is null)
                throw new ArgumentNullException(nameof(input));

            int width = input.Width;
            int height = input.Height;

            // Determine the bounding box of non-transparent pixels.
            int minX = width, minY = height, maxX = 0, maxY = 0;
            bool found = false;
            input.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        if (row[x].A > 0)
                        {
                            found = true;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            });

            // If no non-transparent pixel was found, return a blank 512x512.
            if (!found)
            {
                return new Image<Rgba32>(512, 512, new Rgba32(0, 0, 0, 0));
            }

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;

            // Crop the image to the bounding box.
            Image<Rgba32> cropped = input.Clone(ctx => ctx.Crop(new Rectangle(minX, minY, cropWidth, cropHeight)));

            // If the cropped image is larger than 512 in either dimension, resize it down while preserving aspect ratio.
            double scale = 1.0;
            if (cropWidth > 512 || cropHeight > 512)
            {
                scale = Math.Min(512.0 / cropWidth, 512.0 / cropHeight);
            }
            int newWidth = (int)Math.Round(cropWidth * scale);
            int newHeight = (int)Math.Round(cropHeight * scale);
            Image<Rgba32> resized;
            if (scale != 1.0)
            {
                resized = cropped.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                cropped.Dispose();
            }
            else
            {
                resized = cropped;
            }

            // Create a 512x512 transparent canvas.
            Image<Rgba32> canvas = new Image<Rgba32>(512, 512, new Rgba32(0, 0, 0, 0));
            // Center the resized image on the canvas.
            int offsetX = (512 - newWidth) / 2;
            int offsetY = (512 - newHeight) / 2;
            canvas.Mutate(ctx => ctx.DrawImage(resized, new Point(offsetX, offsetY), 1f));
            resized.Dispose();

            return canvas;
        }


        // Optional test method.
        public static void Test()
        {
            string inputPath = "../input.png";
            string outputPath = "../output.png";
            using Image<Rgba32> img = Image.Load<Rgba32>(inputPath);
            // Here we enable drop shadow with custom parameters.
            Image<Rgba32> processed = ProcessSticker(img, tolerance: 100, extraMargin: 3, inwardEdge: 3,
                                                      addDropShadow: true,
                                                      dropShadowOffsetX: 5,
                                                      dropShadowOffsetY: 5,
                                                      dropShadowBlurSigma: 3f,
                                                      dropShadowOpacity: 0.5f);

            processed = CropAndEnsure512x512(processed);
            processed.SaveAsPng(outputPath, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
            Console.WriteLine($"Processed image saved: {outputPath}");
            processed.Save("../output.webp", new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        }
    }
}
