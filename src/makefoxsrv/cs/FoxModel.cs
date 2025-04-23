using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxModel
    {
        // Static dictionary to hold all global models by name
        private static Dictionary<string, FoxModel> globalModels = new Dictionary<string, FoxModel>();

        // Core model identity properties
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public string SHA256 { get; private set; }
        public string Title { get; private set; }
        public string FileName { get; private set; }
        public string Config { get; private set; }

        // Populated from settings cache
        public bool IsPremium { get; private set; } = false;
        public string? Notes { get; private set; }
        public string? Description { get; private set; }
        public string? InfoUrl { get; private set; }
        public int PageNumber { get; private set; } = 1;

        public int MaxDimension { get; private set; } = 1024;
        public int DefaultSteps { get; private set; } = FoxSettings.Get<int>("DefaultSteps");
        public decimal DefaultCFG { get; private set; } = FoxSettings.Get<decimal>("DefaultCFGScale");
        public int HiresSteps { get; private set; } = 15;
        public decimal HiresDenoise { get; private set; } = 0.33M;

        private DateTime? _settingsCacheTime = null;
        private static readonly TimeSpan _settingsCacheDuration = TimeSpan.FromHours(1);
        private Dictionary<string, string>? _settingsCache = null;

        private static readonly object _modelLock = new();

        // Workers that are running this model
        private HashSet<int> workersRunningModel;

        // Constructor (private, because we want to control creation via GetOrCreateModel)
        private FoxModel(string name, string hash, string sha256, string title, string fileName, string config)
        {
            Name = name;
            Hash = hash;
            SHA256 = sha256;
            Title = title;
            FileName = fileName;
            Config = config;
            workersRunningModel = new HashSet<int>();
        }

        public void AddWorker(int workerId)
        {
            workersRunningModel.Add(workerId);
        }

        public static async Task<FoxModel> GetOrCreateModel(string name, string hash, string sha256, string title, string fileName, string config)
        {
            lock (_modelLock)
            {
                if (globalModels.TryGetValue(name, out var existing))
                    return existing;
            }

            // Create and load outside the lock
            var newModel = new FoxModel(name, hash, sha256, title, fileName, config);
            await newModel.LoadAllSettingsAsync();

            lock (_modelLock)
            {
                // Double-check insert in case another thread beat us to it while we loaded
                if (!globalModels.ContainsKey(name))
                {
                    globalModels[name] = newModel;
                }

                return globalModels[name];
            }
        }

        public static long ClearCache()
        {
            long count = 0;

            foreach (var model in globalModels.Values)
            {
                _ = model.LoadAllSettingsAsync(); // Fire-and-forget refresh
                count++;
            }

            return count;
        }

        public async Task LoadAllSettingsAsync()
        {
            _settingsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            string query = @"
                SELECT setting, value
                FROM model_settings
                WHERE model = @modelName";

            using var cmd = new MySqlCommand(query, SQL);
            cmd.Parameters.AddWithValue("@modelName", this.Name);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string setting = reader.GetString("setting");
                string value = reader.IsDBNull("value") ? "" : reader.GetString("value").Trim();

                _settingsCache[setting] = value;
            }

            _settingsCacheTime = DateTime.Now;

            // Populate cached values
            IsPremium = TryConvertSetting<bool?>("IsPremium") ?? false;
            Notes = TryConvertSetting<string?>("Notes");
            Description = TryConvertSetting<string?>("Description");
            InfoUrl = TryConvertSetting<string?>("InfoUrl");
            PageNumber = TryConvertSetting<int?>("PageNumber") ?? 1;

            MaxDimension = TryConvertSetting<int?>("MaxDimension") ?? 1024;
            DefaultSteps = TryConvertSetting<int?>("DefaultSteps") ?? FoxSettings.Get<int>("DefaultSteps");
            DefaultCFG = TryConvertSetting<decimal?>("DefaultCFGScale") ?? FoxSettings.Get<decimal>("DefaultCFGScale");
            HiresSteps = TryConvertSetting<int?>("HiresSteps") ?? 15;
            HiresDenoise = TryConvertSetting<decimal?>("HiresDenoise") ?? 0.33M;
        }

        public async Task RefreshModelSettingsAsync()
        {
            if (_settingsCache is null || _settingsCacheTime is null || (DateTime.Now - _settingsCacheTime) > _settingsCacheDuration)
            {
                await LoadAllSettingsAsync();
            }
        }

        private T? TryConvertSetting<T>(string key)
        {
            if (_settingsCache is null)
                throw new InvalidOperationException("Model settings are not initialized.");

            if (_settingsCache.TryGetValue(key, out var value))
            {
                try
                {
                    Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                    object converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                    return (T)converted;
                }
                catch
                {
                    // Log conversion error if necessary
                }
            }
            return default;
        }

        //public class ModelCounts
        //{
        //    public record Counts(int Daily, int Weekly)
        //    {
        //        public Counts Increment() => this with
        //        {
        //            Daily = Daily + 1,
        //            Weekly = Weekly + 1
        //        };

        //        public Counts ResetDaily() => this with { Daily = 0 };
        //        public Counts ResetWeekly() => this with { Weekly = 0 };
        //    }

        //    public Dictionary<FoxModel, Counts> Usage { get; } = new();

        //    public Counts Get(FoxModel model)
        //    {
        //        return Usage.TryGetValue(model, out var counts)
        //            ? counts
        //            : new Counts(0, 0);
        //    }

        //    public void Record(FoxModel model)
        //    {
        //        Usage[model] = Get(model).Increment();
        //    }

        //    public void ResetDaily()
        //    {
        //        foreach (var model in Usage.Keys.ToList())
        //            Usage[model] = Usage[model].ResetDaily();
        //    }

        //    public void ResetWeekly()
        //    {
        //        foreach (var model in Usage.Keys.ToList())
        //            Usage[model] = Usage[model].ResetWeekly();
        //    }
        //}

        public static async Task<int> GetUserWeeklyCount(FoxUser user)
        {
            int userWeeklyLimit = 0;

            var premiumModels = GetAllLoadedModels()
                .Where(m => m.IsPremium)
                .Select(m => m.Name)
                .ToList();

            if (premiumModels.Any())
            {
                // Build the IN clause with parameter names: @model0, @model1, etc.
                var modelParams = premiumModels
                    .Select((m, i) => $"@model{i}")
                    .ToList();

                using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
                await SQL.OpenAsync();

                using var cmd = new MySqlCommand($@"
                    SELECT COUNT(id) AS total_gens
                    FROM queue
                    WHERE
                      uid = @uid
                      AND status = 'FINISHED'
                      AND YEARWEEK(date_added, 1) = YEARWEEK(CURDATE(), 1)
                      AND model IN ({string.Join(",", modelParams)})
                ", SQL);

                cmd.Parameters.AddWithValue("@uid", user.UID);

                // Add each model parameter
                for (int i = 0; i < premiumModels.Count; i++)
                    cmd.Parameters.AddWithValue($"@model{i}", premiumModels[i]);

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    userWeeklyLimit = Convert.ToInt32(result);
            }

            return userWeeklyLimit;
        }

        public async Task<int> GetUserDailyCount(FoxUser user)
        {
           
            int userDailyLimit = 0;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand(@"
                SELECT 
                  COUNT(id) AS total_gens
                FROM 
                  queue
                WHERE 
                  uid = @uid
                  AND model = @model
                  AND status = 'FINISHED'
                  AND date_added >= CURDATE()
                ", SQL))
            {
                cmd.Parameters.AddWithValue("@uid", user.UID);
                cmd.Parameters.AddWithValue("@model", this.Name);

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    userDailyLimit = Convert.ToInt32(result);
            }

            return userDailyLimit;
        }

        [Flags]
        public enum DenyReason
        {
            None = 0,
            DailyLimitReached = 1 << 0,
            WeeklyLimitReached = 1 << 1,
            RestrictedModel = 1 << 2,
        }

        public record LimitCheckResult(bool IsAllowed, DenyReason Reason)
        {
            public static implicit operator bool(LimitCheckResult result) => result.IsAllowed;

            public bool Has(DenyReason reason) => Reason.HasFlag(reason);
        }

        public async Task<LimitCheckResult> IsUserAllowed(FoxUser user)
        {

            if (!IsPremium || user.CheckAccessLevel(AccessLevel.PREMIUM))
                return new(true, DenyReason.None);

            var reason = DenyReason.None;

            var userDailyCount = await GetUserDailyCount(user);
            var userWeeklyCount = await GetUserWeeklyCount(user);

            if (userDailyCount >= 10)
                reason |= DenyReason.DailyLimitReached;

            if (userWeeklyCount >= 100)
                reason |= DenyReason.WeeklyLimitReached;

            return new(reason == DenyReason.None, reason);
        }

        public static FoxModel? GetModelByName(string modelName) =>
            globalModels.TryGetValue(modelName, out var model) ? model : null;

        public static Dictionary<string, FoxModel> GetAvailableModels()
        {
            lock (_modelLock)
            {
                return globalModels.Values
                    .Where(m => m.workersRunningModel.Any())
                    .ToDictionary(m => m.Name, m => m);
            }
        }

        public List<int> GetWorkersRunningModel() => workersRunningModel.ToList();

        public static List<FoxModel> GetAllLoadedModels() => globalModels.Values.ToList();

        public static List<FoxModel> GetModelsByParameter(Func<FoxModel, bool> filter) =>
            globalModels.Values.Where(filter).ToList();

        public static void WorkerWentOffline(int workerId)
        {
            // Optional future logic
        }
    }
}
