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
using Swan.Logging;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace makefoxsrv;

public class FoxONNXImageTagger
{
    private static readonly List<InferenceSession> _sessions;
    private static readonly Dictionary<int, string> _tags;
    private static int _nextSessionIndex = 0;
    private static readonly object _sessionLock = new();

    static FoxONNXImageTagger()
    {
        string modelPath = "../models/JTP_PILOT2-e3-vit_so400m_patch14_siglip_384_fp16.onnx";
        //int gpuCount = PhysicalGPU.GetPhysicalGPUs().Count();

        var gpuList = FoxNVMLWrapper.GetAllDevices();
        var gpuCount = gpuList.Count();

        FoxLog.WriteLine($"Initializing ONNX model on {gpuCount} GPU(s)...");

        var sessionList = new InferenceSession[gpuCount];
        var workingSessions = new ConcurrentBag<InferenceSession>();

        Parallel.ForEach(gpuList, (gpu) =>
        {
            try
            {
                var gpuName = gpu.Name;

                FoxLog.WriteLine($"Initializing ONNX session on GPU {gpu.Index}: {gpuName}");

                // Set GPU device ID for this session
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                options.AppendExecutionProvider_CUDA((int)gpu.Index);
                options.AppendExecutionProvider_CPU(); // Optional fallback within GPU session

                var session = new InferenceSession(modelPath, options);
                workingSessions.Add(session);
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Failed to init ONNX session on GPU {gpu.Index}: {ex.Message}");
            }
        });

        if (workingSessions.Count == 0)
        {
            FoxLog.WriteLine("No usable GPU sessions. Falling back to CPU-only inference.");

            try
            {
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                options.AppendExecutionProvider_CPU();

                var cpuSession = new InferenceSession(modelPath, options);
                workingSessions.Add(cpuSession);
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Failed to init CPU-only ONNX session: {ex.Message}");
                throw;
            }
        }

        _sessions = workingSessions.ToList();

        // Use first successful session to load tags
        _tags = LoadTagsFromONNX(_sessions[0]);
        if (_tags == null || _tags.Count == 0)
            throw new Exception("Failed to load tags from ONNX model metadata.");

        FoxLog.WriteLine($"ONNX model loaded on {_sessions.Count} device(s): {modelPath}");
    }



    public static void Start()
    {
        // This method is intentionally left empty.
        // Initialization is done in the static constructor.
        // Any additional startup logic can be added here if needed.

        FoxLog.WriteLine("ONNX Image Tagger initialized.");
    }

    private InferenceSession GetNextSession()
    {
        lock (_sessionLock)
        {
            var session = _sessions[_nextSessionIndex];
            _nextSessionIndex = (_nextSessionIndex + 1) % _sessions.Count;
            return session;
        }
    }

    /// <summary>
    /// Processes an input Image<Rgba32> and returns the top predicted tags with their scores.
    /// (The ONNX model is assumed to output 9083 logits already activated by the GatedHead.)
    /// </summary>
    public Dictionary<string, float> ProcessImage(Image<Rgba32> image, float weightThreshold = 0.2f)
    {
        // Preprocess image to FP32
        float[] inputTensor = PreprocessImage(image);

        // Get model input metadata
        var session = GetNextSession();

        var inputName = session.InputMetadata.Keys.First();
        var expectedType = session.InputMetadata[inputName].ElementDataType; // Get ONNX-defined type

        Console.WriteLine($"🔍 Model expects input type: {expectedType}");

        // ✅ Correctly detect FP16 model
        bool isFp16Model = (expectedType == TensorElementType.Float16);

        NamedOnnxValue inputData;

        if (isFp16Model)
        {
            Float16[] halfTensor = Array.ConvertAll(inputTensor, f => (Float16)f);
            inputData = NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<Float16>(halfTensor, new int[] { 1, 3, 384, 384 }));
            Console.WriteLine("✅ Using FP16 (Half) input tensor.");
        }
        else
        {
            inputData = NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<float>(inputTensor, new int[] { 1, 3, 384, 384 }));
            Console.WriteLine("✅ Using FP32 (Float) input tensor.");
        }

        var inputs = new List<NamedOnnxValue> { inputData };

        Console.WriteLine("🔍 Checking model outputs...");
        foreach (var output in session.OutputMetadata)
        {
            Console.WriteLine($"✅ Model Output Name: {output.Key}, Type: {output.Value.ElementType}");
        }

        // **Run inference**

        var startTime = DateTime.Now;
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(new List<NamedOnnxValue> { inputData });
        var elapsedTime = DateTime.Now - startTime;

        FoxLog.Write($"Inference completed in {elapsedTime.TotalMilliseconds:F2} ms. ", LogLevel.INFO);

        // ✅ Convert FP16 output back to FP32 if necessary
        float[] finalScores = isFp16Model
            ? results.First().AsEnumerable<Float16>().Select(h => (float)h).ToArray()  // Convert FP16 -> FP32
            : results.First().AsEnumerable<float>().ToArray();

        results.Dispose();

        if (finalScores.Length != _tags.Count)
        {
            throw new Exception($"Output length {finalScores.Length} does not match tag count {_tags.Count}.");
        }

        int numClasses = finalScores.Length;

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
}
