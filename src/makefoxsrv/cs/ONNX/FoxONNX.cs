//using System;
//using System.Linq;
//using Microsoft.ML.OnnxRuntime;
//using Microsoft.ML.OnnxRuntime.Tensors;
//using BERTTokenizers;

//public static class FoxONNX
//{
//    private static InferenceSession? _session;
//    private static BertCasedCustomVocabulary _tokenizer;
//    static FoxONNX()
//    {
//        _tokenizer = new BertCasedCustomVocabulary("../models/mxbai-embed-large-v1-vocab.txt");
//    }

//    public static void Start()
//    {
//        string modelPath = "../models/mxbai-embed-large-v1.onnx";

//        var options = new SessionOptions();

//        // Enable CUDA (GPU) execution
//        try
//        {
//            options.AppendExecutionProvider_CUDA(); // Use GPU, if available
//            options.AppendExecutionProvider_CPU();

//            Console.WriteLine("ONNX Runtime: Using CUDA for inference.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"CUDA not available, falling back to CPU: {ex.Message}");

//            options = new SessionOptions();
//            options.AppendExecutionProvider_CPU();
//        }

//        _session = new InferenceSession(modelPath, options);

//        DateTime startTime = DateTime.UtcNow; 
//        GenerateEmbedding("This is a test embedding");
//        Console.WriteLine($"ONNX Runtime: Startup test completed in {(DateTime.UtcNow - startTime).TotalMilliseconds} ms.");
//        startTime = DateTime.UtcNow;
//        GenerateEmbedding("This is a test embedding", 128);
//        Console.WriteLine($"ONNX Runtime: Test (128) completed in {(DateTime.UtcNow - startTime).TotalMilliseconds} ms.");
//        startTime = DateTime.UtcNow;
//        GenerateEmbedding("This is a test embedding", 256);
//        Console.WriteLine($"ONNX Runtime: Test (256) completed in {(DateTime.UtcNow - startTime).TotalMilliseconds} ms.");
//        startTime = DateTime.UtcNow;
//        GenerateEmbedding("This is a test embedding", 512);
//        Console.WriteLine($"ONNX Runtime: Test (512) completed in {(DateTime.UtcNow - startTime).TotalMilliseconds} ms.");
//    }

//    public static float[] GenerateEmbedding(string text, int seqLength = 128)
//    {
//        var encoded = _tokenizer.Encode(seqLength, text);

//        var inputIds = new DenseTensor<long>(encoded.Select(t => t.InputIds).ToArray(), new[] { 1, seqLength });
//        var attentionMask = new DenseTensor<long>(encoded.Select(t => t.AttentionMask).ToArray(), new[] { 1, seqLength });
//        var tokenTypeIds = new DenseTensor<long>(new long[seqLength], new[] { 1, seqLength });

//        var inputs = new[]
//        {
//            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
//            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
//            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
//        };

//        using (var results = _session.Run(inputs))
//        {
//            var outputTensor = results.First().AsTensor<float>();
//            // The output tensor is of shape [1, 128, 1024]
//            // Extract the [CLS] embedding (first token: first 1024 floats)
//            var outputArray = outputTensor.ToArray();
//            const int hiddenSize = 1024;
//            float[] sentenceEmbedding = new float[hiddenSize];
//            Array.Copy(outputArray, 0, sentenceEmbedding, 0, hiddenSize);
//            return sentenceEmbedding;
//        }
//    }



//    public static void Dispose()
//    {
//        _session.Dispose();
//    }
//}
