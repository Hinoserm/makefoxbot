using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public sealed class FoxEmbedding
    {
        private static readonly HttpClient _httpClient;
        private readonly float[] _values;

        // Shared HttpClient setup
        static FoxEmbedding()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com/v1/")
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private FoxEmbedding(float[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public int Length => _values.Length;

        public float this[int index] => _values[index];

        public float[] ToArray() => (float[])_values.Clone();

        // Generate a "[0.3, 0.5, 0.2, 0.1]" string for VEC_FromText()
        public override string ToString()
        {
            // InvariantCulture avoids commas-as-decimals in locales like "de-DE"
            return "[" + string.Join(", ", _values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        }

        public static async Task<FoxEmbedding> CreateAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be null or empty.", nameof(input));

            var payload = new
            {
                model = "text-embedding-3-large",
                input = input
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "embeddings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings?.oaiApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                                                 .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var embedding = doc.RootElement
                               .GetProperty("data")[0]
                               .GetProperty("embedding");

            int len = embedding.GetArrayLength();
            var values = new float[len];
            int i = 0;
            foreach (var v in embedding.EnumerateArray())
            {
                values[i++] = v.GetSingle();
            }

            return new FoxEmbedding(values);
        }

        // Example: cosine similarity
        public static float CosineSimilarity(FoxEmbedding a, FoxEmbedding b)
        {
            if (a.Length != b.Length)
                throw new InvalidOperationException("Embeddings must have the same dimension.");

            double dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a._values[i] * b._values[i];
                normA += a._values[i] * a._values[i];
                normB += b._values[i] * b._values[i];
            }
            return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
        }
    }
}
