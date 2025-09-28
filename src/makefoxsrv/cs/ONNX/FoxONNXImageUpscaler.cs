#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace makefoxsrv
{
    public static class FoxONNXImageUpscaler
    {
        private static readonly List<ONNXSession> _sessions = new();
        private static int _roundRobinIndex = -1;

        public static void Initialize(string modelPath = "../models/realesrgan-x2plus.onnx")
        {

            return; // Do nothing for now

            var gpuList = FoxNVMLWrapper.GetAllDevices();
            FoxLog.WriteLine($"Initializing ONNX model on {gpuList.Count()} GPU(s)...");

            var workingSessions = new ConcurrentBag<ONNXSession>();

            Parallel.ForEach(gpuList, gpu =>
            {
                try
                {
                    var gpuName = gpu.Name;

                    FoxLog.WriteLine($"Initializing ONNX session on GPU {gpu.Index}: {gpuName}");

                    var options = new SessionOptions();
                    options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    options.AppendExecutionProvider_CUDA((int)gpu.Index);
                    options.AppendExecutionProvider_CPU();

                    var session = new InferenceSession("modelPath", options);
                    var onnxSession = new ONNXSession(session, gpu);

                    workingSessions.Add(onnxSession);
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

                    var cpuSession = new ONNXSession(new InferenceSession(modelPath, options), null);
                    workingSessions.Add(cpuSession);
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, $"Failed to init CPU-only ONNX session: {ex.Message}");
                    throw;
                }
            }

            _sessions.Clear();
            _sessions.AddRange(workingSessions);

            FoxLog.WriteLine($"ONNX model loaded on {_sessions.Count} device(s): {modelPath}");
        }

        private static InferenceSession GetNextSession()
        {
            var index = Interlocked.Increment(ref _roundRobinIndex);
            return _sessions[index % _sessions.Count].Session;
        }

        public static Image<Rgba32> Upscale(Image<Rgba32> input)
        {

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.AppendExecutionProvider_CUDA();
            options.AppendExecutionProvider_CPU();

            using var session = new InferenceSession("../models/realesrgan-x2plus.onnx", options);

            var inputTensor = ConvertImageToTensor(input);

            var inputName = session.InputMetadata.Keys.First();
            var inputs = new[] { NamedOnnxValue.CreateFromTensor<float>(inputName, inputTensor) };
            using var results = session.Run(inputs);

            var outputTensor = (DenseTensor<float>)results.First().Value;
            return ConvertTensorToImage(outputTensor);
        }

        private static Tensor<float> ConvertImageToTensor(Image<Rgba32> image)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, image.Width, image.Height });

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = row[x];
                        tensor[0, 0, x, y] = pixel.R / 255f;
                        tensor[0, 1, x, y] = pixel.G / 255f;
                        tensor[0, 2, x, y] = pixel.B / 255f;
                    }
                }
            });

            return tensor;
        }

        private static Image<Rgba32> ConvertTensorToImage(Tensor<float> tensor)
        {
            int width = tensor.Dimensions[2];
            int height = tensor.Dimensions[3];
            var image = new Image<Rgba32>(width, height);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        byte r = (byte)(Math.Clamp(tensor[0, 0, x, y], 0, 1) * 255f);
                        byte g = (byte)(Math.Clamp(tensor[0, 1, x, y], 0, 1) * 255f);
                        byte b = (byte)(Math.Clamp(tensor[0, 2, x, y], 0, 1) * 255f);
                        row[x] = new Rgba32(r, g, b, 255);
                    }
                }
            });

            return image;
        }
    }

    public sealed class ONNXSession
    {
        public InferenceSession Session { get; }
        public dynamic? Gpu { get; } // type whatever FoxNVMLWrapper returns

        public ONNXSession(InferenceSession session, dynamic? gpu)
        {
            Session = session;
            Gpu = gpu;
        }
    }
}
