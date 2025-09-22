using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv
{
    public static class FoxCivitaiRequests
    {
        public class CivitaiRequestItem
        {
            public ulong Id { get; set; }
            public required FoxCivitai.CivitaiInfoItem InfoItem { get; set; }
            public required FoxUser RequestedBy { get; set; }
            public required DateTime DateRequested { get; set; }
            public FoxUser? ApprovedBy { get; set; }
            public DateTime? DateApproved { get; set; }
            public FoxUser? InstalledBy { get; set; }
            public DateTime? DateInstalled { get; set; }

            public async Task SaveAsync()
            {
                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();

                await SaveAsync(connection);
            }

            public async Task SaveAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
            {
                const string query = @"
                    INSERT INTO civitai_requests (id, cid, uid, date_requested, approved_by_uid, date_approved, installed_by_uid, date_installed)
                    VALUES (@id, @cid, @uid, @date_requested, @approved_by_uid, @date_approved, @installed_by_uid, @date_installed)
                    ON DUPLICATE KEY UPDATE
                        cid = VALUES(cid),
                        uid = VALUES(uid),
                        date_requested = VALUES(date_requested),
                        approved_by_uid = VALUES(approved_by_uid),
                        date_approved = VALUES(date_approved),
                        installed_by_uid = VALUES(installed_by_uid),
                        date_installed = VALUES(date_installed);
                    SELECT LAST_INSERT_ID();
                ";

                using var cmd = new MySqlCommand(query, connection, transaction);

                cmd.Parameters.AddWithValue("@id", this.Id);
                cmd.Parameters.AddWithValue("@cid", this.InfoItem.Id);
                cmd.Parameters.AddWithValue("@uid", this.RequestedBy.UID);
                cmd.Parameters.AddWithValue("@date_requested", this.DateRequested);
                cmd.Parameters.AddWithValue("@approved_by_uid", this.ApprovedBy?.UID ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_approved", this.DateApproved ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@installed_by_uid", this.InstalledBy?.UID ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date_installed", this.DateInstalled ?? (object)DBNull.Value);

                var result = await cmd.ExecuteScalarAsync();
                if (this.Id == 0 && result != null)
                    this.Id = Convert.ToUInt64(result);
            }
        }

        private static readonly Regex _linkRegex = new(
            @"https:\/\/civitai\.com\/models\/(?<modelId>\d+)(?:\/[^\s\?]+)?(?:\?modelVersionId=(?<versionId>\d+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static List<(int ModelId, int? VersionId)> ParseFromMessage(string message)
        {
            var results = new List<(int ModelId, int? VersionId)>();

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

                results.Add((modelId, versionId));
            }

            return results;
        }

        public static async Task<List<CivitaiRequestItem>> ParseRequestAsync(string message, FoxUser user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var parsedLinks = ParseFromMessage(message);

            if (parsedLinks.Count == 0)
                return new List<CivitaiRequestItem>(); // Return empty list if no links found

            var civitaiInfoResults = await FetchAllVersionsAsync(parsedLinks);

            var requestList = new List<CivitaiRequestItem>();

            foreach (var civitaiInfo in civitaiInfoResults)
            {
                var requestItem = new CivitaiRequestItem
                {
                    InfoItem = civitaiInfo,
                    RequestedBy = user,
                    DateRequested = DateTime.Now
                };

                requestList.Add(requestItem);
            }

            return requestList;
        }

        public static async Task<List<FoxCivitai.CivitaiInfoItem>> FetchAllVersionsAsync(List<(int ModelId, int? VersionId)> modelLinks)
        {
            var uniqueModels = modelLinks
                .Select(x => x.ModelId)
                .Distinct()
                .Select(id => (id, (int?)null))
                .ToList();

            var civitaiInfoResults = await FoxCivitai.FetchCivitaiInfoAsync(uniqueModels, maxParallel: 4);

            return civitaiInfoResults;
        }

        public static async Task InsertRequestItemsAsync(List<CivitaiRequestItem> requestItems)
        {
            if (requestItems is null || requestItems.Count == 0)
                return;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using var transaction = await SQL.BeginTransactionAsync();

            try
            {
                foreach (var request in requestItems)
                {
                    await request.InfoItem.SaveAsync(SQL, transaction);

                    await request.SaveAsync(SQL, transaction);
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public static async Task DownloadItemAsync(FoxCivitai.CivitaiFileItem fileItem, string destinationPath)
        {
            if (fileItem == null || string.IsNullOrWhiteSpace(fileItem.DownloadUrl))
                throw new ArgumentException("Invalid file item or missing download URL.");

            var finalPath = Path.Combine("..", "data", destinationPath);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeFoxSrv");

            if (FoxMain.settings?.CivitaiApiKey is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.CivitaiApiKey);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var response = await client.GetAsync(fileItem.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
        }

        public record DownloadItem(CivitaiRequestItem Request, FoxCivitai.CivitaiFileItem File, string FileName);


        // This nightmarish function is responsible for ensuring that the downloaded files don't have conflicting names.
        public static List<DownloadItem> PrepareDownloadList(List<FoxCivitaiRequests.CivitaiRequestItem> requestItems)
        {
            var downloadList = new List<DownloadItem>();

            var items = requestItems
                .Where(x => !string.IsNullOrWhiteSpace(x.InfoItem.primaryFile?.Name))
                .ToList();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Preload existing names from FoxLORAs
            var existingLoraNames = new HashSet<string>(
                FoxLORAs.GetAllLORAs()
                    .Select(l => Path.GetFileNameWithoutExtension(l.Filename ?? string.Empty))
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase
            );

            // Preload used names from other items in the list (excluding self)
            foreach (var item in items)
            {
                var file = item.InfoItem.primaryFile;
                if (file == null || string.IsNullOrWhiteSpace(file.Name))
                    continue;

                var ext = Path.GetExtension(file.Name);
                var baseName = Path.GetFileNameWithoutExtension(file.Name!)!;
                var originalBaseName = baseName;

                bool IsConflict(string name, FoxCivitai.CivitaiInfoItem current)
                {
                    return used.Contains(name) ||
                           existingLoraNames.Contains(name) ||
                           items.Any(x => !ReferenceEquals(x, current) &&
                                          x.InfoItem.primaryFile != null &&
                                          Path.GetFileNameWithoutExtension(x.InfoItem.primaryFile.Name ?? string.Empty)
                                             .Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                           (item.InfoItem.Type == FoxCivitai.CivitaiAssetType.LORA && FoxLORAs.GetLorasByFilename(name).Count > 0);
                }

                int suffixIndex = 1;
                var suffix = item.InfoItem.BaseModel?.Trim().ToLowerInvariant() switch
                {
                    "illustrious" => "IL",
                    "pony" => "Pony",
                    "noobai" => "nai",
                    "sdxl" or "sdxl 1.0" or "sdxl lightning" => "XL",
                    _ when item.InfoItem.BaseModel?.StartsWith("sdxl ", StringComparison.OrdinalIgnoreCase) == true => "XL",
                    _ => "FB"
                };

                if (IsConflict(baseName, item.InfoItem))
                {
                    string candidate;

                    do
                    {
                        candidate = $"{originalBaseName}_{suffix}{(suffixIndex > 1 ? suffixIndex.ToString() : "")}";
                        suffixIndex++;
                    }
                    while (IsConflict(candidate, item.InfoItem));

                    baseName = candidate;
                }

                downloadList.Add(new DownloadItem(item, file, baseName + ext));

                used.Add(baseName);
            }

            return downloadList;
        }

        public static List<CivitaiRequestItem> FetchAlreadyInstalled(List<CivitaiRequestItem> requests)
        {
            var installed = new List<CivitaiRequestItem>();

            foreach (var request in requests)
            {
                var primaryHash = request?.InfoItem?.primaryFile?.SHA256;

                if (request is null || request?.InfoItem is null || string.IsNullOrWhiteSpace(primaryHash))
                    continue;

                var matches = FoxLORAs.GetLorasByHash(primaryHash);

                if (matches.Any())
                    installed.Add(request);
            }

            return installed;
        }

        public enum RequestStatus
        {
            Any,
            Pending,
            Approved,
            Installed
        }

        public static async Task<List<CivitaiRequestItem>> FetchAllRequestsAsync(RequestStatus requestStatus = RequestStatus.Any)
        {
            var results = new List<CivitaiRequestItem>();

            using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT r.id, r.cid, r.uid, r.date_requested, r.approved_by_uid, r.date_approved, r.installed_by_uid, r.date_installed
                FROM civitai_requests r
            ";

            switch (requestStatus)
            {
                case RequestStatus.Pending:
                    query += " WHERE r.date_approved IS NULL AND r.date_installed IS NULL";
                    break;
                case RequestStatus.Approved:
                    query += " WHERE r.date_approved IS NOT NULL AND r.date_installed IS NULL";
                    break;
                case RequestStatus.Installed:
                    query += " WHERE r.date_installed IS NOT NULL";
                    break;
                case RequestStatus.Any:
                    // Do nothing
                default:
                    break;
            }

            using var cmd = new MySqlCommand(query, connection);
            using var reader = await cmd.ExecuteReaderAsync();

            var civitaiInfoMap = new Dictionary<ulong, FoxCivitai.CivitaiInfoItem>();

            while (await reader.ReadAsync())
            {
                ulong id = (ulong)reader.GetInt64("id");
                ulong cid = (ulong)reader.GetInt64("cid");
                ulong uid = (ulong)reader.GetInt64("uid");
                DateTime dateRequested = reader.GetDateTime("date_requested");
                ulong? approvedByUid = reader["approved_by_uid"] is DBNull ? null : (ulong?)reader.GetInt64("approved_by_uid");
                DateTime? dateApproved = reader["date_approved"] is DBNull ? null : (DateTime?)reader.GetDateTime("date_approved");
                ulong? installedByUid = reader["installed_by_uid"] is DBNull ? null : (ulong?)reader.GetInt64("installed_by_uid");
                DateTime? dateInstalled = reader["date_installed"] is DBNull ? null : (DateTime?)reader.GetDateTime("date_installed");

                if (!civitaiInfoMap.TryGetValue(cid, out var civitaiInfo))
                {
                    civitaiInfo = await FoxCivitai.CivitaiInfoItem.LoadByCidAsync(cid);
                    if (civitaiInfo != null)
                        civitaiInfoMap[cid] = civitaiInfo;
                }

                if (civitaiInfo == null)
                    continue;

                var user = await FoxUser.GetByUID(uid);

                if (user == null)
                    continue;

                var requestItem = new CivitaiRequestItem
                {
                    Id = id,
                    InfoItem = civitaiInfo,
                    RequestedBy = user,
                    DateRequested = dateRequested,
                    ApprovedBy = approvedByUid.HasValue ? await FoxUser.GetByUID(approvedByUid.Value) : null,
                    DateApproved = dateApproved,
                    InstalledBy = installedByUid.HasValue ? await FoxUser.GetByUID(installedByUid.Value) : null,
                    DateInstalled = dateInstalled
                };

                results.Add(requestItem);
            }

            return results;
        }

        public static Dictionary<FoxCivitai.CivitaiAssetType, List<CivitaiRequestItem>> GroupByType(List<CivitaiRequestItem> items)
        {
            return items
                .GroupBy(x => x.InfoItem.Type)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}
