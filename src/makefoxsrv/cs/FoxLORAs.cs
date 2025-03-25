#nullable enable

using MySqlConnector;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace makefoxsrv
{
    internal class FoxLORAs
    {
        internal class LoraInfo
        {
            public required string Filename { get; set; }
            public required string Hash { get; set; }
            public string? Name { get; set; }
            public string? TriggerWords { get; set; }
            public string? BaseModel { get; set; }
            public string? Alias { get; set; }
            public int? CivitaiId { get; set; }
            public int? CivitaiModelId { get; set; }
            public string? CivitaiUrl =>
                CivitaiId.HasValue && CivitaiModelId.HasValue
                ? $"https://civitai.com/models/{CivitaiModelId}?modelVersionId={CivitaiId}"
                : null;

            public HashSet<FoxWorker> Workers { get; set; } = new(FoxWorkerComparer.Instance);
        }

        private static readonly Dictionary<(string Hash, string Filename), LoraInfo> _lorasByHash = new();
        private static readonly Dictionary<string, List<LoraInfo>> _lorasByFilename = new(StringComparer.OrdinalIgnoreCase);
        public static bool LorasLoaded = false;

        public static async Task StartupLoad()
        {
            var rootDir = FoxSettings.Get<string?>("LoraPath");

            if (String.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
            {
                FoxLog.WriteLine("[LORA] LoraPath is not set or does not exist. Skipping LORA loading.");
                LorasLoaded = false;
                return;
            }

            // Step 1: Load from MySQL
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                string query = @"SELECT hash, filename, base_model, name, trigger_words, civitai_id, civitai_model_id FROM lora_info";

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
                        TriggerWords = reader.IsDBNull("trigger_words") ? null : reader.GetString("trigger_words"),
                        Workers = new(FoxWorkerComparer.Instance)
                    };

                    _lorasByHash[(hash, lora.Filename)] = lora;

                    var filenameKey = lora.Filename;
                    if (!_lorasByFilename.TryGetValue(filenameKey, out var list))
                    {
                        list = new List<LoraInfo>();
                        _lorasByFilename[filenameKey] = list;
                    }

                    list.Add(lora);
                }
            }

            // Step 2: Scan filesystem
            var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase));

            using var insertConn = new MySqlConnection(FoxMain.sqlConnectionString);
            await insertConn.OpenAsync();

            foreach (var file in files)
            {
                var filename = Path.GetFileName(file);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);

                // Skip hashing if filename already known
                if (_lorasByFilename.TryGetValue(nameWithoutExt, out var existingList) &&
                    existingList.Any(l => l.Filename.Equals(nameWithoutExt, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var hash = ComputeSHA256(file);

                if (_lorasByHash.ContainsKey((hash, nameWithoutExt)))
                    continue;

                var lora = new LoraInfo
                {
                    Filename = nameWithoutExt,
                    Hash = hash,
                    Name = null,
                    BaseModel = null,
                    CivitaiId = null,
                    CivitaiModelId = null,
                    TriggerWords = null,
                    Workers = new(FoxWorkerComparer.Instance)
                };

                await DownloadCivitaiInfo(lora);

                using var insertCmd = new MySqlCommand(@"
                    INSERT INTO lora_info (hash, filename, base_model, trigger_words, name, civitai_id, civitai_model_id)
                    VALUES (@hash, @filename, @base_model, @trigger_words, @name, @civitai_id, @civitai_model_id)", insertConn);

                insertCmd.Parameters.AddWithValue("@hash", lora.Hash);
                insertCmd.Parameters.AddWithValue("@filename", lora.Filename);
                insertCmd.Parameters.AddWithValue("@base_model", lora.BaseModel);
                insertCmd.Parameters.AddWithValue("@name", lora.Name);
                insertCmd.Parameters.AddWithValue("@civitai_id", lora.CivitaiId);
                insertCmd.Parameters.AddWithValue("@civitai_model_id", lora.CivitaiModelId);
                insertCmd.Parameters.AddWithValue("@trigger_words", lora.TriggerWords);

                await insertCmd.ExecuteNonQueryAsync();

                _lorasByHash[(hash, lora.Filename)] = lora;

                if (!_lorasByFilename.TryGetValue(nameWithoutExt, out var list))
                {
                    list = new List<LoraInfo>();
                    _lorasByFilename[nameWithoutExt] = list;
                }

                list.Add(lora);
            }


            // Fill in any missing CivitAI info (in case we missed it in previous runs)
            _ = UpdateMissingCivitaiInfo();

            LorasLoaded = true;
        }

        public static async Task UpdateMissingCivitaiInfo()
        {
            foreach (var lora in _lorasByHash.Values)
            {
                if (lora.CivitaiId != null)
                    continue;

                await DownloadCivitaiInfo(lora);
            }
        }

        public static async Task DownloadCivitaiInfo(LoraInfo lora)
        {
            using var http = new HttpClient();
            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

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

                var words = obj["trainedWords"]?.Values<string>();
                if (words != null)
                    lora.TriggerWords ??= string.Join(", ", words.ToList());

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
                cmd.Parameters.AddWithValue("@trigger_words", lora.TriggerWords);
                cmd.Parameters.AddWithValue("@hash", lora.Hash);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to fetch/update for {lora.Filename}: {ex.Message}");
            }
        }

        private static string ComputeSHA256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }


        public static void RegisterWorkerByFilename(FoxWorker worker, string filenameWithoutExtension, string? alias = null)
        {
            if (_lorasByFilename.TryGetValue(filenameWithoutExtension, out var loras))
            {
                foreach (var lora in loras)
                {
                    lora.Alias ??= alias; // Set alias if not already set
                    lora.Workers.Add(worker);
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
                lora.Workers.Remove(worker);
            }
        }

        public static IReadOnlyCollection<LoraInfo> GetAllLORAs() => _lorasByHash.Values;

        public static IEnumerable<LoraInfo> GetLorasByHash(string hash) =>
            _lorasByHash
                .Where(kv => kv.Key.Hash == hash)
                .Select(kv => kv.Value);

        public static IReadOnlyList<LoraInfo> GetLorasByFilename(string filenameWithoutExtension) =>
            _lorasByFilename.TryGetValue(filenameWithoutExtension, out var list)
                ? list
                : Array.Empty<LoraInfo>();

        private class FoxWorkerComparer : IEqualityComparer<FoxWorker>
        {
            public static readonly FoxWorkerComparer Instance = new();

            public bool Equals(FoxWorker? x, FoxWorker? y) => x is not null && y is not null && x.ID == y.ID;

            public int GetHashCode(FoxWorker obj) => obj.ID.GetHashCode();
        }
    }
}
