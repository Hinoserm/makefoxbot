using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Text.Json;

namespace makefoxsrv;

public class FoxONNXImageTagger
{
    private static readonly InferenceSession _session;
    // Maps index (int) -> tag (string) loaded from ONNX metadata key "tags_json"
    private static readonly Dictionary<int, string> _tags;

    static FoxONNXImageTagger()
    {
        string modelPath = "../models/JTP_PILOT2-e3-vit_so400m_patch14_siglip_384.onnx";

        try
        {
            var options = new SessionOptions();
            //options.AppendExecutionProvider_CUDA(); // Use CUDA
            //options.AppendExecutionProvider_CPU();

            _session = new InferenceSession(modelPath);

            _tags = LoadTagsFromONNX(_session);
            if (_tags == null || _tags.Count == 0)
            {
                throw new Exception("Failed to load tags from ONNX model metadata.");
            }

            FoxLog.WriteLine($"ONNX Model Loaded: {modelPath}");
        }
        catch (Exception ex)
        {
            FoxLog.WriteLine($"❌ Failed to load ONNX model: {ex.Message} \r\n {ex.InnerException?.Message} \r\n {ex.InnerException?.StackTrace}", LogLevel.ERROR);
            throw;
        }
    }

    /// <summary>
    /// Processes an input Image<Rgba32> and returns the top predicted tags with their scores.
    /// (The ONNX model is assumed to output 9083 logits already activated by the GatedHead.)
    /// </summary>
    public Dictionary<string, float> ProcessImage(Image<Rgba32> image, float weightThreshold = 0.2f)
    {
        float[] inputTensor = PreprocessImage(image);
        var inputData = new DenseTensor<float>(inputTensor, new int[] { 1, 3, 384, 384 });
        // Use the actual input name from the model metadata.
        string inputName = _session.InputMetadata.Keys.First();
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputData) };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
        float[] finalScores = results.First().AsEnumerable<float>().ToArray();
        results.Dispose();

        if (finalScores.Length != _tags.Count)
        {
            throw new Exception($"Output length {finalScores.Length} does not match tag count {_tags.Count}.");
        }

        int numClasses = finalScores.Length;  // Should always be 9083

        // Sort indices descending by final score.
        int[] sortedIndices = Enumerable.Range(0, numClasses)
                                          .OrderByDescending(i => finalScores[i])
                                          .ToArray();

        Dictionary<string, float> predictions = new();
        foreach (int idx in sortedIndices)
        {
            if (finalScores[idx] >= weightThreshold && _tags.TryGetValue(idx, out string tag))
            {
                predictions[tag] = finalScores[idx];
            }
        }

        return predictions;
    }

    /// <summary>
    /// Preprocesses an Image<Rgba32> exactly as in the original demo:
    /// 1. Fit((384,384)): Resize while preserving aspect ratio and pad to target dimensions (with white padding).
    /// 2. CompositeAlpha(0.5): Composite the image over a white background (simulate alpha blending).
    /// 3. CenterCrop((384,384)): Crop the center.
    /// 4. Convert to tensor in CHW order with normalization: (x/255 - 0.5)/0.5.
    /// </summary>
    private float[] PreprocessImage(Image<Rgba32> image)
    {
        // Clone the image so as not to alter the original.
        using Image<Rgba32> clone = image.Clone();
        // Fit: resize and pad to 384x384 (using white padding)
        using Image<Rgba32> fitted = Fit(clone, 384, 384, grow: true, padColor: Color.White);
        // CompositeAlpha: composite over white background
        using Image<Rgba32> comp = CompositeAlpha(fitted, Color.White);
        // CenterCrop: crop exactly 384x384 (if necessary)
        using Image<Rgba32> cropped = CenterCrop(comp, 384, 384);

        // Optionally save the preprocessed image for debugging:
        cropped.Save("test-output.png");

        // Convert the cropped image to a tensor (CHW order)
        float[] tensor = ConvertToTensor(cropped);
        return tensor;
    }

    /// <summary>
    /// Fit: Resize the image while preserving aspect ratio, and pad to target dimensions using the specified padColor.
    /// </summary>
    private Image<Rgba32> Fit(Image<Rgba32> image, int boundWidth, int boundHeight, bool grow, Color? padColor)
    {
        int origWidth = image.Width;
        int origHeight = image.Height;
        float hscale = (float)boundHeight / origHeight;
        float wscale = (float)boundWidth / origWidth;
        if (!grow)
        {
            hscale = Math.Min(hscale, 1f);
            wscale = Math.Min(wscale, 1f);
        }
        float scale = Math.Min(hscale, wscale);
        int newWidth = Math.Min((int)Math.Round(origWidth * scale), boundWidth);
        int newHeight = Math.Min((int)Math.Round(origHeight * scale), boundHeight);

        // Resize the image.
        Image<Rgba32> resized = image.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));

        // Pad the resized image to the target dimensions.
        if (padColor.HasValue)
        {
            Image<Rgba32> padded = new Image<Rgba32>(boundWidth, boundHeight, padColor.Value);
            // Draw the resized image onto the padded image, centered.
            int padX = (boundWidth - newWidth) / 2;
            int padY = (boundHeight - newHeight) / 2;
            padded.Mutate(ctx => ctx.DrawImage(resized, new Point(padX, padY), 1f));
            resized.Dispose();
            return padded;
        }
        else
        {
            return resized;
        }
    }

    /// <summary>
    /// CompositeAlpha: For each pixel with an alpha channel, composite it over the given background color.
    /// </summary>
    private Image<Rgba32> CompositeAlpha(Image<Rgba32> image, Color backgroundColor)
    {
        // Create a clone to work on.
        Image<Rgba32> result = image.Clone();

        // Process each pixel row to composite the image over the background color.
        result.ProcessPixelRows(pixelAccessor =>
        {
            for (int y = 0; y < pixelAccessor.Height; y++)
            {
                Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 pixel = ref row[x];
                    float alpha = pixel.A / 255f;
                    // Composite: new = pixel * alpha + background * (1 - alpha)

                    var clrPixel = backgroundColor.ToPixel<Rgba32>();
                    pixel.R = (byte)Math.Round(pixel.R * alpha + clrPixel.R * (1 - alpha));
                    pixel.G = (byte)Math.Round(pixel.G * alpha + clrPixel.G * (1 - alpha));
                    pixel.B = (byte)Math.Round(pixel.B * alpha + clrPixel.B * (1 - alpha));
                    pixel.A = 255;
                }
            }
        });
        
        return result;
    }

    /// <summary>
    /// CenterCrop: Crop the image to exactly targetWidth x targetHeight, centered.
    /// If the image is smaller, it is resized to the target dimensions.
    /// </summary>
    private Image<Rgba32> CenterCrop(Image<Rgba32> image, int targetWidth, int targetHeight)
    {
        if (image.Width < targetWidth || image.Height < targetHeight)
        {
            // Resize up if needed.
            image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
            return image;
        }
        int x = (image.Width - targetWidth) / 2;
        int y = (image.Height - targetHeight) / 2;
        return image.Clone(ctx => ctx.Crop(new Rectangle(x, y, targetWidth, targetHeight)));
    }

    /// <summary>
    /// ConvertToTensor: Converts an image to a float array in CHW order.
    /// Normalization: (x/255 - 0.5)/0.5 maps [0,255] to [-1, 1].
    /// </summary>
    private float[] ConvertToTensor(Image<Rgba32> image)
    {
        int width = image.Width;
        int height = image.Height;
        float[] result = new float[3 * width * height];

        // Process each row.
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Calculate the pixel index in CHW format.
                    int pixelIndex = y * width + x;
                    Rgba32 pixel = row[x];
                    // For channel R
                    result[pixelIndex] = (pixel.R / 255f - 0.5f) / 0.5f;
                    // For channel G: offset by width*height
                    result[width * height + pixelIndex] = (pixel.G / 255f - 0.5f) / 0.5f;
                    // For channel B: offset by 2*width*height
                    result[2 * width * height + pixelIndex] = (pixel.B / 255f - 0.5f) / 0.5f;
                }
            }
        });
        return result;
    }

    /// <summary>
    /// Loads tag mapping from ONNX metadata.
    /// Expects metadata key "tags_json" containing a JSON string formatted like:
    /// {"anthro": 0, "female": 1, "male": 2, ...}
    /// This method reverses the mapping to index -> tag.
    /// </summary>
    private static Dictionary<int, string> LoadTagsFromONNX(InferenceSession session)
    {
        var metadata = session.ModelMetadata.CustomMetadataMap;
        if (metadata.TryGetValue("tags_json", out string jsonTags))
        {
            Console.WriteLine("✅ Metadata 'tags_json' found.");
            var tagDict = JsonSerializer.Deserialize<Dictionary<string, int>>(jsonTags);
            return tagDict.ToDictionary(kvp => kvp.Value, kvp => kvp.Key.Replace("_", " "));
        }
        Console.WriteLine("⚠ Metadata 'tags_json' NOT found.");
        return new Dictionary<int, string>();
    }

    public static void Test()
    {
        string modelPath = "../models/JTP_PILOT2-e3-vit_so400m_patch14_siglip_384.onnx";
        string imagePath = "test.jpg";

        using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
        FoxONNXImageTagger tagger = new FoxONNXImageTagger();
        var predictions = tagger.ProcessImage(image);

        Console.WriteLine("\n🔹 Predicted Tags with Scores:");
        foreach (var kv in predictions)
        {
            Console.WriteLine($"{kv.Key}: {kv.Value * 100:F1}%");
        }
    }
}
