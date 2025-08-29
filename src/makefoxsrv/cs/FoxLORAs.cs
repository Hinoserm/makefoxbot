#nullable enable

using MySqlConnector;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using TL;

namespace makefoxsrv
{
    internal class FoxLORAs
    {
        internal class LoraInfo
        {
            public required string Filename { get; set; }
            public required string Hash { get; set; }
            public string? Name { get; set; }
            public List<string>? TriggerWords { get; set; }
            public string? Description { get; set; } // Optional description for the LORA
            public string? BaseModel { get; set; }
            public string? Alias { get; set; }
            public int? CivitaiId { get; set; }
            public int? CivitaiModelId { get; set; }
            public string? CivitaiUrl =>
                CivitaiId.HasValue && CivitaiModelId.HasValue
                ? $"https://civitai.com/models/{CivitaiModelId}?modelVersionId={CivitaiId}"
                : null;
            public List<string>? ImageURLs { get; set; }

            public ConcurrentDictionary<int, FoxWorker> Workers { get; set; } = new();
        }

        private static readonly ConcurrentDictionary<(string Hash, string Filename), LoraInfo> _lorasByHash = new();
        private static readonly ConcurrentDictionary<string, ImmutableList<LoraInfo>> _lorasByFilename = new();
        public static bool LorasLoaded = false;

        public static async Task StartupLoad()
        {
            var rootDir = FoxSettings.Get<string?>("LoraPath");

            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
            {
                FoxLog.WriteLine("[LORA] LoraPath is not set or does not exist. Skipping LORA loading.");
                LorasLoaded = false;
                return;
            }

            var imageLoadSemaphore = new SemaphoreSlim(25);
            var imageLoadTasks = new List<Task>();

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                string query = @"SELECT hash, filename, base_model, name, description, trigger_words, civitai_id, civitai_model_id FROM lora_info";

                using var cmd = new MySqlCommand(query, SQL);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var hash = reader.GetString("hash");

                    var lora = new LoraInfo
                    {
                        Hash = hash,
                        Filename = reader.GetString("filename"),
                        BaseModel = reader.IsDBNull("base_model") ? null : reader.GetString("base_model"),
                        Name = reader.IsDBNull("name") ? null : reader.GetString("name"),
                        CivitaiId = reader.IsDBNull("civitai_id") ? null : reader.GetInt32("civitai_id"),
                        CivitaiModelId = reader.IsDBNull("civitai_model_id") ? null : reader.GetInt32("civitai_model_id"),
                        Description = reader.IsDBNull("description") ? null : reader.GetString("description"),
                        TriggerWords = reader.IsDBNull("trigger_words")
                            ? null
                            : reader.GetString("trigger_words").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    };

                    await imageLoadSemaphore.WaitAsync();

                    var imageTask = Task.Run(async () =>
                    {
                        try
                        {
                            await LoadLoraImageUrlsFromDB(lora);
                        }
                        catch (Exception ex)
                        {
                            FoxLog.WriteLine($"[LORA] Failed to load image URLs for {lora.Filename}: {ex.Message}");
                        }
                        finally
                        {
                            imageLoadSemaphore.Release();
                        }
                    });

                    imageLoadTasks.Add(imageTask);

                    _lorasByHash[(hash, lora.Filename)] = lora;


                    var filenameKey = lora.Filename;
                    _lorasByFilename.AddOrUpdate(filenameKey, _ => ImmutableList.Create(lora), (_, existing) => existing.Add(lora));
                }
            }

            await Task.WhenAll(imageLoadTasks);

            LorasLoaded = true;
            _ = LoadHashes();
        }


        public static string NormalizeLoraTags(string prompt, out List<string> missingLoras)
        {
            missingLoras = new List<string>();

            var regex = new Regex(@"<lora:([^:>]+)(?::([^>]+))?>", RegexOptions.IgnoreCase);
            var matches = regex.Matches(prompt);

            var replacements = new Dictionary<string, string>();

            foreach (Match match in matches)
            {
                string originalName = match.Groups[1].Value;
                string? extra = match.Groups[2].Success ? match.Groups[2].Value : null;

                string? normalizedKey = null;

                //normalizedKey = _lorasByFilename.Keys.FirstOrDefault(k => string.Equals(k, originalName, StringComparison.OrdinalIgnoreCase));

                if (_lorasByFilename.ContainsKey(originalName))
                    normalizedKey = originalName;

                if (normalizedKey != null)
                {
                    string normalizedTag = extra != null
                        ? $"<lora:{normalizedKey}:{extra}>"
                        : $"<lora:{normalizedKey}>";

                    replacements[match.Value] = normalizedTag;
                }
                else if (!missingLoras.Contains(originalName))
                {
                    missingLoras.Add(originalName);
                }
            }

            foreach (var kvp in replacements)
            {
                prompt = prompt.Replace(kvp.Key, kvp.Value);
            }

            return prompt;
        }

        private static async Task LoadHashes()
        {
            var rootDir = FoxSettings.Get<string?>("LoraPath");

            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
            {
                FoxLog.WriteLine("[LORA] LoraPath is not set or does not exist. Skipping LORA loading.");
                return;
            }

            var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var semaphore = new SemaphoreSlim(8); // limit to 4 concurrent workers
            var tasks = new List<Task>();

            foreach (var file in files)
            {
                var filename = Path.GetFileName(file);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);

                // Skip hashing if filename already known
                if (_lorasByFilename.ContainsKey(nameWithoutExt))
                    continue;

                await semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var insertConn = new MySqlConnection(FoxMain.sqlConnectionString);
                        await insertConn.OpenAsync();

                        var hash = ComputeSHA256(file);

                        if (_lorasByHash.ContainsKey((hash, nameWithoutExt)))
                            return;

                        var lora = new LoraInfo
                        {
                            Filename = nameWithoutExt,
                            Hash = hash,
                            Name = null,
                            BaseModel = null,
                            CivitaiId = null,
                            CivitaiModelId = null,
                            Description = null,
                            TriggerWords = null,
                        };

                        await DownloadCivitaiInfo(lora);

                        FoxLog.WriteLine($"[LORA] Found New LORA: {lora.Filename} ({lora.CivitaiUrl})");

                        using var insertCmd = new MySqlCommand(@"
                        INSERT INTO lora_info (hash, filename, base_model, trigger_words, name, description, civitai_id, civitai_model_id)
                        VALUES (@hash, @filename, @base_model, @trigger_words, @name, @description, @civitai_id, @civitai_model_id)", insertConn);

                        insertCmd.Parameters.AddWithValue("@hash", lora.Hash);
                        insertCmd.Parameters.AddWithValue("@filename", lora.Filename);
                        insertCmd.Parameters.AddWithValue("@base_model", (object?)lora.BaseModel ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@name", (object?)lora.Name ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@description", (object?)lora.Description ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@civitai_id", (object?)lora.CivitaiId ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@civitai_model_id", (object?)lora.CivitaiModelId ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@trigger_words", lora.TriggerWords is { Count: > 0 } ? string.Join(", ", lora.TriggerWords) : DBNull.Value);

                        insertCmd.ExecuteNonQuery();


                        _lorasByHash[(hash, lora.Filename)] = lora;

                        _lorasByFilename.AddOrUpdate(nameWithoutExt, _ => ImmutableList.Create(lora), (_, existing) => existing.Add(lora));
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine($"[LORA] Error processing {file}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            await UpdateMissingCivitaiInfo();
            await DownloadMissingLoraImages();
        }

        public static async Task UpdateMissingCivitaiInfo()
        {
            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            foreach (var lora in _lorasByHash.Values)
            {
                if (lora.CivitaiId != null && lora.ImageURLs != null)
                    continue;

                try
                {
                    await DownloadCivitaiInfo(lora);

                    // Update the DB
                    using var cmd = new MySqlCommand(@"
                        UPDATE lora_info
                        SET civitai_id = @civitai_id,
                            civitai_model_id = @civitai_model_id,
                            name = @name,
                            base_model = @base_model,
                            trigger_words = @trigger_words
                        WHERE hash = @hash", conn);

                    cmd.Parameters.AddWithValue("@civitai_id", lora.CivitaiId);
                    cmd.Parameters.AddWithValue("@civitai_model_id", lora.CivitaiModelId);
                    cmd.Parameters.AddWithValue("@name", lora.Name);
                    cmd.Parameters.AddWithValue("@base_model", lora.BaseModel);
                    cmd.Parameters.AddWithValue("@trigger_words", lora.TriggerWords is { Count: > 0 } ? string.Join(", ", lora.TriggerWords) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@hash", lora.Hash);

                    await cmd.ExecuteNonQueryAsync();

                    await SaveLoraImageUrls(lora);
                }
                catch
                {
                    // Ignore errors for individual LORAs, we don't want to halt the entire process
                    FoxLog.WriteLine($"Failed to update Civitai info for {lora.Filename} (hash: {lora.Hash})");
                }
            }
        }

        public static async Task DownloadCivitaiInfo(LoraInfo lora)
        {
            using var http = new HttpClient();

            try
            {
                var url = $"https://civitai.com/api/v1/model-versions/by-hash/{lora.Hash}";
                var response = await http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return;

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                lora.CivitaiId = obj["id"]?.Value<int>();
                lora.CivitaiModelId = obj["modelId"]?.Value<int>();
                lora.Name ??= obj["model"]?["name"]?.ToString(); // fallback
                lora.BaseModel ??= obj["baseModel"]?.ToString();

                // Extract all image URLs from the response
                var imageUrls = obj["images"]?
                    .Select(img => img["url"]?.ToString())
                    .Where(url => !string.IsNullOrEmpty(url))
                    .Select(url => url!) // Cast non-null strings
                    .ToList();

                // If we have URLs, assign them, otherwise set to null
                lora.ImageURLs = imageUrls?.Count > 0 ? imageUrls : null;

                var words = obj["trainedWords"]?.Values<string>();
                if (words is not null && words.Count() > 0)
                    lora.TriggerWords ??= words.ToList();
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to fetch/update for {lora.Filename}: {ex.Message}");
            }
        }

        private static async Task SaveLoraImageUrls(LoraInfo lora)
        {
            try
            {
                if (lora.ImageURLs is null || lora.ImageURLs.Count < 1)
                    return; // Nothing to save

                using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                await conn.OpenAsync();

                // First, delete any existing image URLs for this LORA
                using (var deleteCmd = new MySqlCommand(
                    "DELETE FROM lora_image_urls WHERE lora_hash = @hash", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@hash", lora.Hash);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // Insert all new image URLs
                foreach (var imageUrl in lora.ImageURLs)
                {
                    using var insertCmd = new MySqlCommand(
                        "INSERT INTO lora_image_urls (lora_hash, image_url) " +
                        "VALUES (@hash, @url)", conn);

                    insertCmd.Parameters.AddWithValue("@hash", lora.Hash);
                    insertCmd.Parameters.AddWithValue("@url", imageUrl);

                    await insertCmd.ExecuteNonQueryAsync();
                }

                FoxLog.WriteLine($"[LORA] Saved {lora.ImageURLs.Count} image URLs for LORA {lora.Hash}");
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to save image URLs for LORA {lora.Hash}: {ex.Message}");
            }
        }

        public static async Task LoadLoraImageUrlsFromDB(LoraInfo lora)
        {
            var urls = new List<string>();

            try
            {
                using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                await conn.OpenAsync();

                using var cmd = new MySqlCommand("SELECT image_url FROM lora_image_urls WHERE lora_hash = @hash", conn);
                cmd.Parameters.AddWithValue("@hash", lora.Hash);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    urls.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to retrieve image URLs for LORA {lora.Hash}: {ex.Message}");
            }

            lora.ImageURLs = urls.Count > 0 ? urls : null;
        }

        public static async Task DownloadMissingLoraImages(CancellationToken cancellationToken = default, int maxConcurrent = 10)
        {
            FoxLog.WriteLine("[LORA] Starting download of missing LORA images");

            List<(int id, string url)> pendingDownloads = new();

            // Step 1: Get all URLs that need to be downloaded
            using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync(cancellationToken);

                using var cmd = new MySqlCommand(
                    "SELECT id, image_url FROM lora_image_urls WHERE image IS NULL", conn);

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    pendingDownloads.Add((reader.GetInt32("id"), reader.GetString("image_url")));
                }
            }

            if (pendingDownloads.Count == 0)
            {
                FoxLog.WriteLine("[LORA] No missing LORA images to download");
                return;
            }

            FoxLog.WriteLine($"[LORA] Found {pendingDownloads.Count} LORA images to download");

            // Step 2: Download images with limited concurrency
            var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();
            int successCount = 0;
            int failureCount = 0;

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            foreach (var (id, url) in pendingDownloads)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
                        var uri = new Uri(url);
                        var extension = Path.GetExtension(uri.LocalPath).ToLowerInvariant();

                        if (!validExtensions.Contains(extension))
                        {
                            Interlocked.Increment(ref failureCount);
                            FoxLog.WriteLine($"[LORA] Skipping non-image URL: {url}");
                            return;
                        }

                        // Download the image
                        byte[] imageData = await http.GetByteArrayAsync(url, cancellationToken);

                        if (imageData.Length == 0)
                        {
                            Interlocked.Increment(ref failureCount);
                            FoxLog.WriteLine($"[LORA] Downloaded empty image from {url}");
                            return;
                        }

                        // Save the image to the database
                        using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                        await conn.OpenAsync(cancellationToken);

                        using var cmd = new MySqlCommand("UPDATE lora_image_urls SET image = @image WHERE id = @id", conn);

                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.Add("@image", MySqlDbType.LongBlob).Value = imageData;

                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        Interlocked.Increment(ref successCount);

                        //FoxLog.WriteLine($"[LORA] Successfully downloaded and saved image #{id} ({url.Substring(0, Math.Min(30, url.Length))}...)");
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        FoxLog.WriteLine($"[LORA] Failed to download image #{id} from {url}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            // Wait for all downloads to complete
            await Task.WhenAll(tasks);

            FoxLog.WriteLine($"[LORA] LORA image download complete. Successfully downloaded {successCount} images, {failureCount} failures");
        }

        private static string ComputeSHA256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public static Dictionary<string, List<LoraInfo>> SuggestSimilarLoras(List<string> missingLoraNames, int maxSuggestionsPerMissing = 5)
        {
            const int maxScore = 22; // Lower = stricter
            const int minTokenOverlap = 1;

            var suggestions = new Dictionary<string, List<LoraInfo>>();

            foreach (var rawMissing in missingLoraNames)
            {
                var missingNorm = NormalizeForMatching(rawMissing);
                var missingTokens = Tokenize(rawMissing);

                var scored = new List<(int Score, LoraInfo Info)>();

                var loraList = _lorasByHash.Values
                    .Where(l => l.Workers.Count > 0)
                    .ToList();

                foreach (var info in loraList)
                {
                    var candidates = new List<(string? Field, int Weight)>
                    {
                        (info.Filename, 3),
                        (info.Alias, 3),
                        (info.Name, 2),
                        (info.Description, 1)
                    };

                    if (info.TriggerWords != null)
                        candidates.AddRange(info.TriggerWords.Select(t => (t, 1)));

                    int bestScore = int.MaxValue;

                    foreach (var (field, weight) in candidates)
                    {
                        if (string.IsNullOrWhiteSpace(field))
                            continue;

                        var norm = NormalizeForMatching(field);
                        var tokens = Tokenize(field);

                        int score = LevenshteinDistance(missingNorm, norm);

                        if (norm.Contains(missingNorm))
                            score -= 20;

                        int tokenOverlap = missingTokens.Intersect(tokens).Count();
                        score -= tokenOverlap * 5 * weight;

                        // Require at least 1 overlapping token unless it's a substring
                        if (tokenOverlap < minTokenOverlap && !norm.Contains(missingNorm))
                            continue;

                        if (score < bestScore)
                            bestScore = score;
                    }

                    if (bestScore <= maxScore)
                        scored.Add((bestScore, info));
                }

                var top = scored.OrderBy(x => x.Score)
                                .Select(x => x.Info)
                                .GroupBy(i => (i.Hash, i.Filename))
                                .Select(g => g.First())
                                .Take(maxSuggestionsPerMissing)
                                .ToList();

                if (top.Count > 0)
                    suggestions[rawMissing] = top;
            }

            return suggestions;
        }


        private static string NormalizeForMatching(string input)
        {
            return new string(input
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static HashSet<string> Tokenize(string input)
        {
            return input
                .ToLowerInvariant()
                .Split(new[] { '_', '-', '.', ' ', '[', ']', '(', ')', ',', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
                .Where(t => t.Length > 0)
                .ToHashSet();
        }


        private static int LevenshteinDistance(string s, string t)
        {
            var dp = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++)
                dp[i, 0] = i;

            for (int j = 0; j <= t.Length; j++)
                dp[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1,     // deletion
                                 dp[i, j - 1] + 1),    // insertion
                        dp[i - 1, j - 1] + cost);      // substitution
                }
            }

            return dp[s.Length, t.Length];
        }



        public static void RegisterWorkerByFilename(FoxWorker worker, string filenameWithoutExtension, string? alias = null)
        {
            if (_lorasByFilename.TryGetValue(filenameWithoutExtension, out var loras))
            {
                foreach (var lora in loras)
                {
                    lora.Alias ??= alias; // Set alias if not already set
                    lora.Workers[worker.ID] = worker;
                }
            }
            else
            {
                FoxLog.WriteLine($"[LORA] Worker {worker.ID} reported unknown LORA: {filenameWithoutExtension}");
            }
        }

        public static void RemoveWorker(FoxWorker worker)
        {
            foreach (var lora in _lorasByHash.Values)
            {
                lora.Workers.TryRemove(worker.ID, out _);
            }
        }

        public static IReadOnlyCollection<LoraInfo> GetAllLORAs() => _lorasByHash.Values.ToImmutableArray();

        public static IEnumerable<LoraInfo> GetLorasByHash(string hash) =>
            _lorasByHash
                .Where(kv => string.Equals(kv.Key.Hash, hash, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value);

        public static IReadOnlyList<LoraInfo> GetLorasByFilename(string filenameWithoutExtension)
        {
            return _lorasByFilename.TryGetValue(filenameWithoutExtension, out var list)
                ? list
                : Array.Empty<LoraInfo>();
        }
    }
}
