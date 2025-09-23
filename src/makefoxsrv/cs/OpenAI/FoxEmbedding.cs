using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace makefoxsrv
{
    /// <summary>
    /// Represents a text embedding vector with utilities for creation, storage, retrieval,
    /// and similarity comparison. Embeddings are cached in MariaDB keyed by a SHA-256 hash
    /// of the normalized input string.
    /// </summary>
    public sealed class FoxEmbedding
    {
        private static readonly HttpClient _httpClient;
        private readonly float[] _values;

        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 30_000 // optional limit to keep memory sane
        });

        // Shared HttpClient setup
        static FoxEmbedding()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com/v1/")
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private FoxEmbedding(float[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        private static string HashToString(byte[] hashBytes) => BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        /// <summary>
        /// The number of dimensions in this embedding vector.
        /// </summary>
        public int Length => _values.Length;

        /// <summary>
        /// Indexer for direct access to the underlying float values.
        /// </summary>
        public float this[int index] => _values[index];


        /// <summary>
        /// Returns a copy of the embedding values as a float array.
        /// </summary>
        public float[] ToArray() => (float[])_values.Clone();

        /// <summary>
        /// Converts the embedding into a string representation formatted for use
        /// with MariaDB’s VEC_FromText() function, e.g. "[0.1, 0.2, ...]".
        /// </summary>
        public override string ToString()
        {
            // InvariantCulture avoids commas-as-decimals in locales like "de-DE"
            return "[" + string.Join(", ", _values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Fetches an embedding from the database by its 32-byte SHA-256 hash.
        /// Returns null if no match is found.
        /// </summary>
        /// <param name="hashBytes">The raw 32-byte SHA-256 hash of the normalized input text.</param>
        public static async Task<FoxEmbedding?> FetchByHashAsync(byte[] hashBytes)
        {
            if (hashBytes == null || hashBytes.Length != 32)
                throw new ArgumentException("Hash must be a 32-byte array.", nameof(hashBytes));

            if (_cache.TryGetValue(HashToString(hashBytes), out FoxEmbedding? cached))
                return cached;

            using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync();

                using (var checkCmd = conn.CreateCommand())
                {
                    checkCmd.CommandText = @"
                        SELECT VEC_ToText(embedding)
                        FROM cached_embeddings
                        WHERE hash = @hash
                        LIMIT 1;";
                    checkCmd.Parameters.Add("@hash", MySqlDbType.Binary, 32).Value = hashBytes;

                    var existing = await checkCmd.ExecuteScalarAsync();
                    if (existing != null && existing != DBNull.Value)
                    {
                        var vecString = existing.ToString()!;
                        var parts = vecString.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var values = parts.Select(p => float.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

                        var foxEmbedding = new FoxEmbedding(values);

                        _cache.Set(HashToString(hashBytes), foxEmbedding, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromHours(1),
                            Size = 1
                        });

                        return foxEmbedding;
                    }
                }
            }

            return null; // Not found
        }

        /// <summary>
        /// Creates an embedding for the given input text. If an embedding already exists
        /// in the database (based on SHA-256 hash), that one is returned. Otherwise,
        /// a new embedding is requested from OpenAI, stored in the database, and returned.
        /// </summary>
        /// <param name="input">The input text to embed.</param>
        public static async Task<FoxEmbedding> CreateAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be null or empty.", nameof(input));

            // Hash of normalized input
            byte[] hashBytes;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input.Trim().ToLowerInvariant()));
            }

            // 1. Try DB first
            var existingEmbedding = await FetchByHashAsync(hashBytes);
            if (existingEmbedding is not null)
                return existingEmbedding;

            // 2. Not in DB → call OpenAI
            var payload = new { model = "text-embedding-3-large", input = input };
            var request = new HttpRequestMessage(HttpMethod.Post, "embeddings");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings?.oaiApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var embedding = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

            int len = embedding.GetArrayLength();
            var valuesNew = new float[len];
            for (int i = 0; i < len; i++)
                valuesNew[i] = embedding[i].GetSingle();

            var foxEmbedding = new FoxEmbedding(valuesNew);

            // 3. Store in DB
            using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync();

                using (var insertCmd = conn.CreateCommand())
                {
                    insertCmd.CommandText = @"
                        INSERT IGNORE INTO cached_embeddings (hash, embedding, date_generated)
                        VALUES (@hash, VEC_FromText(@vec), @now);";
                    insertCmd.Parameters.Add("@hash", MySqlDbType.Binary, 32).Value = hashBytes;
                    insertCmd.Parameters.AddWithValue("@vec", foxEmbedding.ToString());
                    insertCmd.Parameters.AddWithValue("@now", DateTime.Now);

                    await insertCmd.ExecuteNonQueryAsync();
                }
            }

            _cache.Set(HashToString(hashBytes), foxEmbedding, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(24),
                Size = 1
            });

            return foxEmbedding;
        }


        /// <summary>
        /// Computes the cosine similarity between two embeddings. 
        /// Returns a value in [-1,1] where 1 is identical, 0 is orthogonal, and -1 is opposite.
        /// </summary>
        /// <param name="a">First embedding.</param>
        /// <param name="b">Second embedding.</param>
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
