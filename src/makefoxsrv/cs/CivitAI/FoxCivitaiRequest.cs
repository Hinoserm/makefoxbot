using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv
{
    public static class FoxCivitaiRequests
    {
        public enum CivitaiAssetType
        {
            LORA,
            Embedding,
            Model,
            Other,
            Unknown
        }

        public class CivitaiItem
        {
            public int ModelId { get; init; }
            public int? VersionId { get; init; }
            public string? ModelName { get; set; }
            public string? FileName { get; set; }
            public CivitaiAssetType Type { get; set; } = CivitaiAssetType.Unknown;
            public string? DownloadUrl { get; set; }
            public string? TrainedWords { get; set; }
            public string? Description { get; set; }
            public string? BaseModel { get; set; }
            public string? SHA256Hash { get; set; }
            public string? RawJson { get; set; } // stores full API response
            public FoxUser? User { get; set; }

            public DateTime? DateAdded { get; set; }
            public DateTime? DateInstalled { get; set; }
            public string? FilePath { get; set; }

            public CivitaiItem(int modelId, int? versionId = null)
            {
                ModelId = modelId;
                VersionId = versionId;
            }

            public string GetStoragePath()
            {
                if (string.IsNullOrWhiteSpace(BaseModel))
                    return "other";

                var normalized = BaseModel.Trim().ToLowerInvariant();

                return normalized switch
                {
                    "sd 1.5" => "sd",
                    "sd 2.1 768" => "sd21",
                    "flux" => "flux",
                    "noobai" => "sdxl/nai",
                    "pony" => "sdxl/pony",
                    "illustrious" => "sdxl/illustrious",
                    "sdxl lightning" => "sdxl/lightning",
                    "sdxl 1.0" => "sdxl/other",
                    "sdxl" => "sdxl/other",
                    _ when normalized.StartsWith("sdxl ") => "sdxl/other",
                    _ => "other"
                };
            }

            public string GetShortBaseModel()
            {
                if (string.IsNullOrWhiteSpace(BaseModel))
                    return "FB";

                var normalized = BaseModel.Trim().ToLowerInvariant();

                return normalized switch
                {
                    "illustrious" => "IL",
                    "pony" => "Pony",
                    "noobai" => "nai",
                    "sdxl" or "sdxl 1.0" or "sdxl lightning" => "XL",
                    _ when normalized.StartsWith("sdxl ") => "XL",
                    _ => "FB"
                };
            }

            // Future method:
            // public string GetStoragePath() { ... }
        }

        private static readonly Regex _linkRegex = new(
            @"https:\/\/civitai\.com\/models\/(?<modelId>\d+)(?:\/[^\s\?]+)?(?:\?modelVersionId=(?<versionId>\d+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static List<CivitaiItem> ParseFromMessage(string message, FoxUser? user = null)
        {
            var results = new List<CivitaiItem>();

            foreach (Match match in _linkRegex.Matches(message))
            {
                if (!int.TryParse(match.Groups["modelId"].Value, out var modelId))
                    continue;

                int? versionId = null;
                if (match.Groups["versionId"].Success &&
                    int.TryParse(match.Groups["versionId"].Value, out var parsedVersionId))
                {
                    versionId = parsedVersionId;
                }

                var item = new CivitaiItem(modelId, versionId)
                {
                    User = user,
                    DateAdded = DateTime.Now
                };

                results.Add(item);
            }

            return results;
        }


        public static async Task<List<CivitaiItem>> FetchAllVersionsAsync(List<CivitaiItem> modelLinks)
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.civitai.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeFoxSrv");

            if (FoxMain.settings?.CivitaiApiKey is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.CivitaiApiKey);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var allItems = new List<CivitaiItem>();
            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var throttler = new SemaphoreSlim(4);
            var tasks = new List<Task>();

            foreach (var item in modelLinks)
            {
                await throttler.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var results = await FetchModelVersionsAsync(client, item, seenHashes);
                        lock (allItems)
                        {
                            allItems.AddRange(results);
                        }
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return allItems;
        }


        private static async Task<List<CivitaiItem>> FetchModelVersionsAsync(HttpClient client, CivitaiItem item, HashSet<string> seenHashes)
        {
            var list = new List<CivitaiItem>();

            try
            {
                var response = await client.GetAsync($"/v1/models/{item.ModelId}?nsfw=true");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                var name = root.GetProperty("name").GetString();

                CivitaiAssetType type = CivitaiAssetType.Unknown;
                if (root.TryGetProperty("type", out var typeProp))
                {
                    type = typeProp.GetString()?.ToUpper() switch
                    {
                        "LORA" => CivitaiAssetType.LORA,
                        "TEXTUALINVERSION" => CivitaiAssetType.Embedding,
                        "CHECKPOINT" => CivitaiAssetType.Model,
                        _ => CivitaiAssetType.Other
                    };
                }

                if (root.TryGetProperty("modelVersions", out var versions))
                {
                    foreach (var version in versions.EnumerateArray())
                    {
                        int versionId = version.GetProperty("id").GetInt32();

                        string? description = version.TryGetProperty("description", out var descProp)
                            ? descProp.GetString()
                            : null;

                        string? baseModel = version.TryGetProperty("baseModel", out var baseProp)
                            ? baseProp.GetString()
                            : null;

                        string? trainedWords = null;
                        if (version.TryGetProperty("trainedWords", out var trainedProp) &&
                            trainedProp.ValueKind == JsonValueKind.Array)
                        {
                            trainedWords = string.Join(", ", trainedProp.EnumerateArray()
                                .Select(t => t.GetString())
                                .Where(s => !string.IsNullOrWhiteSpace(s)));
                        }

                        string? downloadUrl = version.TryGetProperty("downloadUrl", out var dlProp)
                            ? dlProp.GetString()
                            : null;

                        string? selectedFileName = null;
                        string? selectedHash = null;

                        if (version.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                        {
                            foreach (var file in files.EnumerateArray())
                            {
                                if (!file.TryGetProperty("name", out var fileProp))
                                    continue;

                                var nameVal = fileProp.GetString();
                                if (string.IsNullOrWhiteSpace(nameVal))
                                    continue;

                                var ext = System.IO.Path.GetExtension(nameVal).ToLowerInvariant();
                                bool validExt =
                                    (type is CivitaiAssetType.LORA or CivitaiAssetType.Model && ext == ".safetensors") ||
                                    (type == CivitaiAssetType.Embedding && ext == ".pt");

                                if (!validExt)
                                    continue;

                                if (!file.TryGetProperty("hashes", out var hashes) ||
                                    !hashes.TryGetProperty("SHA256", out var hashProp))
                                    continue;

                                var candidateHash = hashProp.GetString();
                                if (string.IsNullOrWhiteSpace(candidateHash))
                                    continue;

                                if (seenHashes.Contains(candidateHash))
                                    continue;

                                // ✅ Valid, not seen before
                                selectedFileName = nameVal;
                                selectedHash = candidateHash;
                                break;
                            }
                        }

                        // If no valid unique file found, skip version
                        if (string.IsNullOrWhiteSpace(selectedHash) || string.IsNullOrWhiteSpace(selectedFileName))
                            continue;

                        seenHashes.Add(selectedHash);

                        list.Add(new CivitaiItem(item.ModelId, versionId)
                        {
                            User = item.User,
                            DateAdded = item.DateAdded ?? DateTime.Now,
                            ModelName = name,
                            FileName = selectedFileName,
                            Type = type,
                            DownloadUrl = downloadUrl,
                            TrainedWords = trainedWords,
                            Description = description,
                            BaseModel = baseModel,
                            SHA256Hash = selectedHash,
                            RawJson = json
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
                return new List<CivitaiItem>();
            }

            return list;
        }

        public static List<(string Original, string Updated)> EnsureUniqueFilenames(List<CivitaiItem> items)
        {
            var renamedList = new List<(string Original, string Updated)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dbFilenames = FetchCivitaiItemsAsync(StatusFilter.All).Result
                .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
                .Select(x => System.IO.Path.GetFileNameWithoutExtension(x.FileName!)!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.FileName))
                    continue;

                var ext = System.IO.Path.GetExtension(item.FileName);
                var originalBase = System.IO.Path.GetFileNameWithoutExtension(item.FileName)!;

                var finalBase = originalBase;

                bool needsRename =
                    usedNames.Contains(originalBase) ||
                    dbFilenames.Contains(originalBase) ||
                    (item.Type == CivitaiAssetType.LORA && FoxLORAs.GetLorasByFilename(originalBase).Count > 0);

                if (needsRename)
                {
                    var suffix = item.GetShortBaseModel();
                    var attempt = $"{originalBase}_{suffix}";

                    if (!usedNames.Contains(attempt) &&
                        !dbFilenames.Contains(attempt) &&
                        (item.Type != CivitaiAssetType.LORA || FoxLORAs.GetLorasByFilename(attempt).Count == 0))
                    {
                        finalBase = attempt;
                    }
                    else
                    {
                        for (int i = 2; ; i++)
                        {
                            var candidate = $"{originalBase}_{suffix}{i}";
                            bool inBatch = usedNames.Contains(candidate);
                            bool inDB = dbFilenames.Contains(candidate);
                            bool alreadyInstalled = item.Type == CivitaiAssetType.LORA &&
                                                    FoxLORAs.GetLorasByFilename(candidate).Count > 0;

                            if (!inBatch && !inDB && !alreadyInstalled)
                            {
                                finalBase = candidate;
                                break;
                            }
                        }
                    }

                    item.FileName = finalBase + ext;
                    renamedList.Add((originalBase, finalBase));
                }

                usedNames.Add(finalBase);
            }

            return renamedList;
        }

        public static List<CivitaiItem> FetchAlreadyInstalled(List<CivitaiItem> loras)
        {
            var installed = new List<CivitaiItem>();

            foreach (var item in loras)
            {
                if (string.IsNullOrWhiteSpace(item.SHA256Hash))
                    continue;

                var matches = FoxLORAs.GetLorasByHash(item.SHA256Hash);
                if (matches.Any())
                {
                    installed.Add(item);
                }
            }

            return installed;
        }

        public static async Task InsertCivitaiItemsAsync(List<CivitaiItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using var transaction = await SQL.BeginTransactionAsync();

            const string query = @"
                INSERT INTO civitai_requests (
                    hash, filename, type, download_url,
                    base_model, model_name, description, trigger_words,
                    civitai_model_id, civitai_version_id, date_added,
                    json_raw, uid, file_path, date_installed
                ) VALUES (
                    @hash, @filename, @type, @download_url,
                    @base_model, @model_name, @description, @trigger_words,
                    @model_id, @version_id, @date_added,
                    @json_raw, @uid, @file_path, @date_installed
                )
                ON DUPLICATE KEY UPDATE
                    filename = VALUES(filename),
                    type = VALUES(type),
                    download_url = VALUES(download_url),
                    base_model = VALUES(base_model),
                    model_name = VALUES(model_name),
                    description = VALUES(description),
                    trigger_words = VALUES(trigger_words),
                    civitai_model_id = VALUES(civitai_model_id),
                    civitai_version_id = VALUES(civitai_version_id),
                    json_raw = VALUES(json_raw),
                    date_added = VALUES(date_added),
                    date_installed = VALUES(date_installed),
                    file_path = VALUES(file_path),
                    uid = VALUES(uid);";

            try
            {
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.SHA256Hash) ||
                        string.IsNullOrWhiteSpace(item.FileName) ||
                        string.IsNullOrWhiteSpace(item.DownloadUrl))
                        continue;

                    using var cmd = new MySqlCommand(query, SQL, transaction);
                    cmd.Parameters.AddWithValue("@hash", item.SHA256Hash);
                    cmd.Parameters.AddWithValue("@filename", item.FileName);
                    cmd.Parameters.AddWithValue("@type", item.Type.ToString().ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@download_url", item.DownloadUrl);
                    cmd.Parameters.AddWithValue("@base_model", item.BaseModel ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_name", item.ModelName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@description", item.Description ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@trigger_words", item.TrainedWords ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_id", item.ModelId);
                    cmd.Parameters.AddWithValue("@version_id", item.VersionId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@date_added", item.DateAdded ?? DateTime.Now);
                    cmd.Parameters.AddWithValue("@date_installed", item.DateInstalled ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@file_path", item.FilePath ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@json_raw", item.RawJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@uid", item.User?.UID ?? (object)DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public enum StatusFilter
        {
            All,
            Installed,
            Uninstalled
        }

        public static async Task<List<CivitaiItem>> FetchCivitaiItemsAsync(StatusFilter filter = StatusFilter.All)
        {
            var items = new List<CivitaiItem>();

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            var query = @"
                SELECT hash, filename, type, download_url,
                       base_model, model_name, description, trigger_words,
                       civitai_model_id, civitai_version_id, json_raw, uid,
                       date_added, date_installed, file_path
                FROM civitai_requests";

            if (filter == StatusFilter.Installed)
                query += " WHERE date_installed IS NOT NULL";
            else if (filter == StatusFilter.Uninstalled)
                query += " WHERE date_installed IS NULL";

            using var cmd = new MySqlCommand(query, SQL);
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
            {
                if (r["hash"] is DBNull || r["filename"] is DBNull || r["download_url"] is DBNull)
                    continue;

                long? uid = r["uid"] is DBNull ? null : Convert.ToInt64(r["uid"]);
                FoxUser? user = uid is not null ? await FoxUser.GetByUID(uid.Value) : null;

                var item = new CivitaiItem(
                    modelId: Convert.ToInt32(r["civitai_model_id"]),
                    versionId: r["civitai_version_id"] is DBNull ? null : Convert.ToInt32(r["civitai_version_id"])
                )
                {
                    SHA256Hash = Convert.ToString(r["hash"]),
                    FileName = Convert.ToString(r["filename"]),
                    DownloadUrl = Convert.ToString(r["download_url"]),
                    BaseModel = r["base_model"] is DBNull ? null : Convert.ToString(r["base_model"]),
                    ModelName = r["model_name"] is DBNull ? null : Convert.ToString(r["model_name"]),
                    Description = r["description"] is DBNull ? null : Convert.ToString(r["description"]),
                    TrainedWords = r["trigger_words"] is DBNull ? null : Convert.ToString(r["trigger_words"]),
                    RawJson = r["json_raw"] is DBNull ? null : Convert.ToString(r["json_raw"]),
                    DateAdded = r["date_added"] is DBNull ? null : Convert.ToDateTime(r["date_added"]),
                    DateInstalled = r["date_installed"] is DBNull ? null : Convert.ToDateTime(r["date_installed"]),
                    FilePath = r["file_path"] is DBNull ? null : Convert.ToString(r["file_path"]),
                    Type = Enum.TryParse<CivitaiAssetType>(
                        Convert.ToString(r["type"]), true, out var parsedType)
                        ? parsedType
                        : CivitaiAssetType.Unknown,
                    User = user
                };

                items.Add(item);
            }

            return items;
        }

        public static async Task DownloadItemAsync(CivitaiItem item, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(item.DownloadUrl))
                throw new ArgumentException("URL must not be null or empty.", nameof(item.DownloadUrl));

            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path must not be null or empty.", nameof(destinationPath));

            var finalPath = Path.Combine(new[] { "..", "data", destinationPath });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeFoxSrv");

            if (FoxMain.settings?.CivitaiApiKey is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.CivitaiApiKey);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Ensure target directory exists
            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var response = await client.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
        }


        public static Dictionary<CivitaiAssetType, List<CivitaiItem>> GroupByType(List<CivitaiItem> items)
        {
            return items
                .GroupBy(i => i.Type)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}
