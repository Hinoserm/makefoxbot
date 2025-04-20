using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
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
