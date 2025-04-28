using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public static class FoxCivitai
    {
        // This is a semaphore to limit the number of concurrent API requests to Civitai.
        private static readonly SemaphoreSlim CivitaiApiSemaphore = new(4);

        // Cache for CivitaiInfoItems to avoid redundant database queries.
        private static readonly Dictionary<ulong, CivitaiInfoItem> _cache = new();
        private static readonly object _cacheLock = new();

        public enum CivitaiAssetType
        {
            LORA,
            Embedding,
            Model,
            Other,
            Unknown
        }

        public class CivitaiFileItem
        {
            public ulong Id { get; set; }
            public CivitaiInfoItem? Parent { get; set; }
            public ulong FileId { get; set; }
            public string? Name { get; set; }
            public ulong? Size { get; set; }
            public string? Type { get; set; }
            public string? Format { get; set; }
            public bool PrimaryFile { get; set; }
            public string? SHA256 { get; set; }
            public string? DownloadUrl { get; set; }
            public DateTime DateAdded { get; set; } = DateTime.Now;
            public DateTime? DateDownloaded { get; set; }
            public DateTime? DateInstalled { get; set; }
            public string? RawJson { get; set; }

            public async Task SaveAsync()
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();
                await SaveAsync(connection, null);
            }

            public async Task SaveAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
            {
                if (Parent == null) throw new InvalidOperationException("Parent cannot be null when saving file.");

                const string query = @"
                    INSERT INTO civitai_model_files
                    (cid, file_id, name, size, type, format, primary_file, hash_sha256, download_url, date_added, date_downloaded, date_installed, json_raw)
                    VALUES
                    (@cid, @file_id, @name, @size, @type, @format, @primary_file, @hash_sha256, @download_url, @date_added, @date_downloaded, @date_installed, @json_raw)
                    ON DUPLICATE KEY UPDATE
                        name = VALUES(name),
                        size = VALUES(size),
                        type = VALUES(type),
                        format = VALUES(format),
                        primary_file = VALUES(primary_file),
                        hash_sha256 = VALUES(hash_sha256),
                        download_url = VALUES(download_url),
                        date_added = VALUES(date_added),
                        date_downloaded = VALUES(date_downloaded),
                        date_installed = VALUES(date_installed),
                        json_raw = VALUES(json_raw);
                ";

                using var cmd = new MySqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@cid", Parent.Id);
                cmd.Parameters.AddWithValue("@file_id", FileId);
                cmd.Parameters.AddWithValue("@name", Name ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@size", Size ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@type", Type ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@format", Format ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@primary_file", PrimaryFile ? 1 : 0);
                cmd.Parameters.AddWithValue("@hash_sha256", SHA256 ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@download_url", DownloadUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_added", DateAdded);
                cmd.Parameters.AddWithValue("@date_downloaded", DateDownloaded ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_installed", DateInstalled ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@json_raw", RawJson ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            internal static async Task<List<CivitaiFileItem>> LoadForParentAsync(CivitaiInfoItem parent, MySqlConnection connection)
            {
                var list = new List<CivitaiFileItem>();

                const string query = @"SELECT * FROM civitai_model_files WHERE cid = @cid";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@cid", parent.Id);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new CivitaiFileItem
                    {
                        Id = (ulong)r.GetInt64("id"),
                        Parent = parent,
                        FileId = (ulong)r.GetInt64("file_id"),
                        Name = r["name"] as string,
                        Size = r["size"] is DBNull ? null : (ulong?)r.GetUInt64("size"),
                        Type = r["type"] as string,
                        Format = r["format"] as string,
                        PrimaryFile = r.GetBoolean("primary_file"),
                        SHA256 = r["hash_sha256"] as string,
                        DownloadUrl = r["download_url"] as string,
                        DateAdded = r.GetDateTime("date_added"),
                        DateDownloaded = r["date_downloaded"] as DateTime?,
                        DateInstalled = r["date_installed"] as DateTime?,
                        RawJson = r["json_raw"] as string
                    });
                }

                return list;
            }

            public async Task DownloadAsync(string destinationPath)
            {
                if (string.IsNullOrWhiteSpace(DownloadUrl))
                    throw new InvalidOperationException("No download URL.");

                if (string.IsNullOrWhiteSpace(destinationPath))
                    throw new InvalidOperationException("Invalid destination path.");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                using var client = new HttpClient();

                if (FoxMain.settings?.CivitaiApiKey is not null)
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.CivitaiApiKey);


                using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
                DateDownloaded = DateTime.Now;
                await this.SaveAsync();
            }
        }

        public class CivitaiImageItem
        {
            public ulong Id { get; set; }
            public CivitaiInfoItem? Parent { get; set; }
            public string? Url { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public string? Hash { get; set; }
            public int? NsfwLevel { get; set; }
            public string? Type { get; set; }
            public DateTime DateAdded { get; set; } = DateTime.Now;
            public string? RawJson { get; set; }

            public async Task SaveAsync()
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();
                await SaveAsync(connection, null);
            }

            public async Task SaveAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
            {
                if (Parent == null)
                    throw new InvalidOperationException("Parent cannot be null when saving image.");

                const string query = @"
                    INSERT INTO civitai_model_images
                    (cid, url, width, height, hash, nsfw_level, type, date_added, json_raw)
                    VALUES
                    (@cid, @url, @width, @height, @hash, @nsfw_level, @type, @date_added, @json_raw)
                    ON DUPLICATE KEY UPDATE
                        url = VALUES(url),
                        width = VALUES(width),
                        height = VALUES(height),
                        hash = VALUES(hash),
                        nsfw_level = VALUES(nsfw_level),
                        type = VALUES(type),
                        date_added = VALUES(date_added),
                        json_raw = VALUES(json_raw);
                ";

                using var cmd = new MySqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@cid", Parent.Id);
                cmd.Parameters.AddWithValue("@url", Url ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@width", Width ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@height", Height ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@hash", Hash ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@nsfw_level", NsfwLevel ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@type", Type ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_added", DateAdded);
                cmd.Parameters.AddWithValue("@json_raw", RawJson ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            internal static async Task<List<CivitaiImageItem>> LoadForParentAsync(CivitaiInfoItem parent, MySqlConnection connection)
            {
                var list = new List<CivitaiImageItem>();

                const string query = @"SELECT * FROM civitai_model_images WHERE cid = @cid";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@cid", parent.Id);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new CivitaiImageItem
                    {
                        Id = (ulong)r.GetInt64("id"),
                        Parent = parent,
                        Url = r["url"] as string,
                        Width = r["width"] is DBNull ? null : (int?)r.GetInt32("width"),
                        Height = r["height"] is DBNull ? null : (int?)r.GetInt32("height"),
                        Hash = r["hash"] as string,
                        NsfwLevel = r["nsfw_level"] is DBNull ? null : (int?)r.GetInt32("nsfw_level"),
                        Type = r["type"] as string,
                        DateAdded = r.GetDateTime("date_added"),
                        RawJson = r["json_raw"] as string
                    });
                }

                return list;
            }
        }

        public class CivitaiInfoItem
        {
            public ulong Id { get; set; }
            public CivitaiAssetType Type { get; set; } = CivitaiAssetType.Unknown;
            public string? BaseModel { get; set; }
            public string? ModelName { get; set; }
            public string? Description { get; set; }
            public string? TriggerWords { get; set; }
            public int? ModelId { get; set; }
            public int? VersionId { get; set; }
            public DateTime DateAdded { get; set; } = DateTime.Now;
            public string? RawJson { get; set; }

            public List<CivitaiFileItem> Files { get; set; } = new();
            public List<CivitaiImageItem> Images { get; set; } = new();

            public async Task SaveAsync()
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();
                await SaveAsync(connection, null);
            }

            public async Task SaveAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
            {
                const string query = @"
                    INSERT INTO civitai_model_info
                    (id, type, base_model, model_name, description, trigger_words,
                     civitai_model_id, civitai_version_id, date_added, json_raw)
                    VALUES
                    (@id, @type, @base_model, @model_name, @description, @trigger_words,
                     @civitai_model_id, @civitai_version_id, @date_added, @json_raw)
                    ON DUPLICATE KEY UPDATE
                        type = VALUES(type),
                        base_model = VALUES(base_model),
                        model_name = VALUES(model_name),
                        description = VALUES(description),
                        trigger_words = VALUES(trigger_words),
                        civitai_model_id = VALUES(civitai_model_id),
                        civitai_version_id = VALUES(civitai_version_id),
                        date_added = VALUES(date_added),
                        json_raw = VALUES(json_raw);
                    SELECT LAST_INSERT_ID();
                ";

                using var cmd = new MySqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@id", Id);
                cmd.Parameters.AddWithValue("@type", Type.ToString().ToUpperInvariant());
                cmd.Parameters.AddWithValue("@base_model", BaseModel ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@model_name", ModelName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@description", Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@trigger_words", TriggerWords ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@civitai_model_id", ModelId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@civitai_version_id", VersionId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_added", DateAdded);
                cmd.Parameters.AddWithValue("@json_raw", RawJson ?? (object)DBNull.Value);

                var result = await cmd.ExecuteScalarAsync();
                if (Id == 0 && result != null)
                    Id = Convert.ToUInt64(result);

                foreach (var file in Files)
                    await file.SaveAsync(connection, transaction);

                foreach (var image in Images)
                    await image.SaveAsync(connection, transaction);

                lock (_cacheLock)
                {
                    _cache[this.Id] = this;
                }
            }


            public static async Task<CivitaiInfoItem?> LoadByCidAsync(ulong cid)
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();
                return await LoadByCidAsync(cid, connection);
            }

            public static async Task<CivitaiInfoItem?> LoadByCidAsync(ulong cid, MySqlConnection connection)
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(cid, out var cached))
                        return cached;
                }

                const string query = "SELECT * FROM civitai_model_info WHERE id = @cid LIMIT 1";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@cid", cid);

                using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return null;

                var item = new CivitaiInfoItem
                {
                    Id = (ulong)r.GetInt64("id"),
                    Type = Enum.TryParse(r["type"] as string, true, out CivitaiAssetType t) ? t : CivitaiAssetType.Unknown,
                    BaseModel = r["base_model"] as string,
                    ModelName = r["model_name"] as string,
                    Description = r["description"] as string,
                    TriggerWords = r["trigger_words"] as string,
                    ModelId = r["civitai_model_id"] is DBNull ? null : (int?)r.GetInt32("civitai_model_id"),
                    VersionId = r["civitai_version_id"] is DBNull ? null : (int?)r.GetInt32("civitai_version_id"),
                    DateAdded = r.GetDateTime("date_added"),
                    RawJson = r["json_raw"] as string
                };

                await r.CloseAsync();

                item.Files = await CivitaiFileItem.LoadForParentAsync(item, connection);
                item.Images = await CivitaiImageItem.LoadForParentAsync(item, connection);

                return item;
            }

            public static async Task<List<CivitaiInfoItem>> LoadByHashAsync(string sha256Hash)
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();
                return await LoadByHashAsync(sha256Hash, connection);
            }

            public static async Task<List<CivitaiInfoItem>> LoadByHashAsync(string sha256Hash, MySqlConnection connection)
            {
                lock (_cacheLock)
                {
                    var cacheResults = _cache.Values
                        .Where(x => x.Files.Any(f => string.Equals(f.SHA256, sha256Hash, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (cacheResults.Count > 0)
                        return cacheResults;
                }

                const string query = @"
                    SELECT DISTINCT i.*
                    FROM civitai_model_info i
                    INNER JOIN civitai_model_files f ON f.cid = i.id
                    WHERE f.hash_sha256 = @hash_sha256;
                ";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@hash_sha256", sha256Hash);

                using var r = await cmd.ExecuteReaderAsync();

                var results = new List<CivitaiInfoItem>();

                while (await r.ReadAsync())
                {
                    var item = new CivitaiInfoItem
                    {
                        Id = (ulong)r.GetInt64("id"),
                        Type = Enum.TryParse<CivitaiAssetType>(r["type"] as string ?? string.Empty, true, out var parsedType) ? parsedType : CivitaiAssetType.Unknown,
                        BaseModel = r["base_model"] as string,
                        ModelName = r["model_name"] as string,
                        Description = r["description"] as string,
                        TriggerWords = r["trigger_words"] as string,
                        ModelId = r["civitai_model_id"] is DBNull ? null : (int?)r.GetInt32("civitai_model_id"),
                        VersionId = r["civitai_version_id"] is DBNull ? null : (int?)r.GetInt32("civitai_version_id"),
                        DateAdded = r.GetDateTime("date_added"),
                        RawJson = r["json_raw"] as string
                    };

                    results.Add(item);
                }

                await r.CloseAsync();

                foreach (var item in results)
                {
                    item.Files = await CivitaiFileItem.LoadForParentAsync(item, connection);
                    item.Images = await CivitaiImageItem.LoadForParentAsync(item, connection);
                }

                return results;
            }

            public CivitaiFileItem? primaryFile
            {
                get
                {
                    if (this.Type == CivitaiAssetType.LORA || this.Type == CivitaiAssetType.Model)
                    {
                        var primary = Files.Find(f => f.PrimaryFile && f.Name?.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) == true);
                        if (primary != null)
                            return primary;

                        var fallback = Files.Find(f => f.Name?.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) == true);
                        if (fallback != null)
                            return fallback;
                    }
                    else if (this.Type == CivitaiAssetType.Embedding)
                    {
                        var primary = Files.Find(f => f.PrimaryFile && f.Name?.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) == true);
                        if (primary != null)
                            return primary;

                        var fallback = Files.Find(f => f.Name?.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) == true);
                        if (fallback != null)
                            return fallback;
                    }

                    return null;
                }
            }

            //public async Task DownloadAsync(string destinationDirectory)
            //{
            //    Directory.CreateDirectory(destinationDirectory);

            //    CivitaiFileItem? file = Files.Find(f => f.PrimaryFile) ?? Files.Find(f =>
            //        (Type == CivitaiAssetType.LORA || Type == CivitaiAssetType.Model) && (f.Name?.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) == true) ||
            //        (Type == CivitaiAssetType.Embedding && (f.Name?.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) == true)));

            //    if (file == null)
            //        throw new InvalidOperationException("No primary or valid file found to download.");

            //    var fileName = file.Name ?? (Guid.NewGuid().ToString());
            //    var destinationPath = Path.Combine(destinationDirectory, fileName);

            //    await file.DownloadAsync(destinationPath);

            //    Filename = fileName;
            //    FilePath = destinationPath;
            //    DateDownloaded = DateTime.Now;
            //    await SaveAsync();
            //}
        }

        public static async Task InitializeCacheAsync()
        {
            var allItems = await FetchAllInfoItemsAsync();

            lock (_cacheLock)
            {
                _cache.Clear();
                foreach (var item in allItems)
                {
                    _cache[item.Id] = item;
                }
            }
        }

        public static async Task<List<CivitaiInfoItem>> FetchAllInfoItemsAsync()
        {
            var list = new List<CivitaiInfoItem>();

            using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
            await connection.OpenAsync();

            const string query = @"
                SELECT i.*, 
                        f.id AS file_id, f.cid AS file_cid, f.file_id AS file_fileid, f.name AS file_name, f.size AS file_size, f.type AS file_type,
                        f.format AS file_format, f.primary_file AS file_primary, f.hash_sha256 AS file_sha256, f.download_url AS file_downloadurl,
                        f.date_added AS file_dateadded, f.date_downloaded AS file_datedownloaded, f.date_installed AS file_dateinstalled, f.json_raw AS file_json,
                        img.id AS img_id, img.cid AS img_cid, img.url AS img_url, img.width AS img_width, img.height AS img_height, img.hash AS img_hash,
                        img.nsfw_level AS img_nsfwlevel, img.type AS img_type, img.date_added AS img_dateadded, img.json_raw AS img_json
                FROM civitai_model_info i
                LEFT JOIN civitai_model_files f ON f.cid = i.id
                LEFT JOIN civitai_model_images img ON img.cid = i.id
                ORDER BY i.id";

            using var cmd = new MySqlCommand(query, connection);
            using var r = await cmd.ExecuteReaderAsync();

            var infoMap = new Dictionary<ulong, CivitaiInfoItem>();

            while (await r.ReadAsync())
            {
                var cid = (ulong)r.GetInt64("id");

                if (!infoMap.TryGetValue(cid, out var info))
                {
                    info = new CivitaiInfoItem
                    {
                        Id = cid,
                        Type = Enum.TryParse<CivitaiAssetType>(r["type"] as string ?? string.Empty, true, out var parsedType) ? parsedType : CivitaiAssetType.Unknown,
                        BaseModel = r["base_model"] as string,
                        ModelName = r["model_name"] as string,
                        Description = r["description"] as string,
                        TriggerWords = r["trigger_words"] as string,
                        ModelId = r["civitai_model_id"] is DBNull ? null : (int?)r.GetInt32("civitai_model_id"),
                        VersionId = r["civitai_version_id"] is DBNull ? null : (int?)r.GetInt32("civitai_version_id"),
                        DateAdded = r.GetDateTime("date_added"),
                        RawJson = r["json_raw"] as string
                    };

                    infoMap[cid] = info;
                    list.Add(info);
                }

                if (r["file_id"] is not DBNull)
                {
                    var file = new CivitaiFileItem
                    {
                        Parent = info,
                        Id = (ulong)r.GetInt64("file_id"),
                        FileId = (ulong)r.GetInt64("file_fileid"),
                        Name = r["file_name"] as string,
                        Size = r["file_size"] is DBNull ? null : (ulong?)r.GetUInt64("file_size"),
                        Type = r["file_type"] as string,
                        Format = r["file_format"] as string,
                        PrimaryFile = r.GetBoolean("file_primary"),
                        SHA256 = r["file_sha256"] as string,
                        DownloadUrl = r["file_downloadurl"] as string,
                        DateAdded = r["file_dateadded"] is DBNull ? DateTime.Now : r.GetDateTime("file_dateadded"),
                        DateDownloaded = r["file_datedownloaded"] is DBNull ? null : (DateTime?)r.GetDateTime("file_datedownloaded"),
                        DateInstalled = r["file_dateinstalled"] is DBNull ? null : (DateTime?)r.GetDateTime("file_dateinstalled"),
                        RawJson = r["file_json"] as string
                    };
                    info.Files.Add(file);
                }

                if (r["img_id"] is not DBNull)
                {
                    var img = new CivitaiImageItem
                    {
                        Parent = info,
                        Id = (ulong)r.GetInt64("img_id"),
                        Url = r["img_url"] as string,
                        Width = r["img_width"] is DBNull ? null : (int?)r.GetInt32("img_width"),
                        Height = r["img_height"] is DBNull ? null : (int?)r.GetInt32("img_height"),
                        Hash = r["img_hash"] as string,
                        NsfwLevel = r["img_nsfwlevel"] is DBNull ? null : (int?)r.GetInt32("img_nsfwlevel"),
                        Type = r["img_type"] as string,
                        DateAdded = r["img_dateadded"] is DBNull ? DateTime.Now : r.GetDateTime("img_dateadded"),
                        RawJson = r["img_json"] as string
                    };
                    info.Images.Add(img);
                }
            }

            return list;
        }

        public static async Task<List<CivitaiInfoItem>> FetchCivitaiInfoAsync(List<(int ModelId, int? VersionId)> requests, int maxParallel = 4)
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.civitai.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeFoxSrv");

            if (FoxMain.settings?.CivitaiApiKey is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.CivitaiApiKey);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var allItems = new List<CivitaiInfoItem>();
            var tasks = new List<Task>();

            foreach (var (modelId, versionId) in requests)
            {
                await CivitaiApiSemaphore.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.GetAsync($"/v1/models/{modelId}?nsfw=true");
                        response.EnsureSuccessStatusCode();

                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);

                        var root = doc.RootElement;
                        string? modelName = root.GetProperty("name").GetString();
                        string? typeRaw = root.GetProperty("type").GetString();
                        var modelType = typeRaw?.ToUpperInvariant() switch
                        {
                            "LORA" => CivitaiAssetType.LORA,
                            "TEXTUALINVERSION" => CivitaiAssetType.Embedding,
                            "CHECKPOINT" => CivitaiAssetType.Model,
                            _ => CivitaiAssetType.Other
                        };

                        if (root.TryGetProperty("modelVersions", out var versions))
                        {
                            foreach (var version in versions.EnumerateArray())
                            {
                                int thisVersionId = version.GetProperty("id").GetInt32();
                                if (versionId.HasValue && thisVersionId != versionId.Value)
                                    continue;

                                var now = DateTime.Now;

                                var info = new CivitaiInfoItem
                                {
                                    ModelId = modelId,
                                    VersionId = thisVersionId,
                                    ModelName = modelName,
                                    Type = modelType,
                                    BaseModel = version.TryGetProperty("baseModel", out var bm) ? bm.GetString() : null,
                                    Description = version.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                                    TriggerWords = version.TryGetProperty("trainedWords", out var tw) && tw.ValueKind == JsonValueKind.Array
                                        ? string.Join(", ", tw.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                                        : null,
                                    DateAdded = now,
                                    RawJson = version.GetRawText()
                                };

                                if (version.TryGetProperty("files", out var filesElement))
                                {
                                    foreach (var file in filesElement.EnumerateArray())
                                    {
                                        var fileItem = new CivitaiFileItem
                                        {
                                            Parent = info,
                                            FileId = (ulong)(file.GetProperty("id").GetInt32()),
                                            Name = file.GetProperty("name").GetString(),
                                            Type = file.GetProperty("type").GetString(),
                                            Size = file.TryGetProperty("sizeKB", out var sz) ? (ulong?)(sz.GetDouble() * 1024) : null,
                                            Format = file.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("format", out var fmt) ? fmt.GetString() : null,
                                            SHA256 = file.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("SHA256", out var sha) ? sha.GetString() : null,
                                            DownloadUrl = file.TryGetProperty("downloadUrl", out var dl) ? dl.GetString() : null,
                                            PrimaryFile = file.TryGetProperty("primary", out var primary) && primary.GetBoolean(),
                                            DateAdded = now,
                                            RawJson = file.GetRawText()
                                        };
                                        info.Files.Add(fileItem);
                                    }
                                }

                                if (version.TryGetProperty("images", out var imagesElement))
                                {
                                    foreach (var img in imagesElement.EnumerateArray())
                                    {
                                        var imageItem = new CivitaiImageItem
                                        {
                                            Parent = info,
                                            Url = img.GetProperty("url").GetString(),
                                            Width = img.TryGetProperty("width", out var width) ? width.GetInt32() : (int?)null,
                                            Height = img.TryGetProperty("height", out var height) ? height.GetInt32() : (int?)null,
                                            Hash = img.TryGetProperty("hash", out var hash) ? hash.GetString() : null,
                                            NsfwLevel = img.TryGetProperty("nsfwLevel", out var nsfw) ? nsfw.GetInt32() : (int?)null,
                                            Type = img.TryGetProperty("type", out var type) ? type.GetString() : null,
                                            DateAdded = now,
                                            RawJson = img.GetRawText()
                                        };
                                        info.Images.Add(imageItem);
                                    }
                                }

                                lock (allItems)
                                {
                                    allItems.Add(info);
                                }

                                if (versionId.HasValue)
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual failures
                    }
                    finally
                    {
                        CivitaiApiSemaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return allItems;
        }
    }
}
