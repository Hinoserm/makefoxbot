using MySqlConnector;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        private static readonly ConcurrentDictionary<string, FoxModel> _globalModels = new(StringComparer.OrdinalIgnoreCase);

        // Private fields


        // From database
        [DbColumn("name")]
        private string? _name;

        [DbColumn("is_premium")]
        private bool _isPremium = false;

        [DbColumn("notes")]
        private string? _notes = null;

        [DbColumn("description")]
        private string? _description = null;

        [DbColumn("info_url")]
        private string? _infoUrl = null;

        [DbColumn("type")]
        private ModelType _type = ModelType.UNKNOWN;

        [DbColumn("category")]
        private string? _category = null;

        [DbColumn("enabled")]
        private bool _enabled = false;


        // From workers

        private string? _hash = null;
        private string? _fileName = null;


        // Used internally

        // Timestamp when this model was last updated from the database
        private DateTime _loadedDate = DateTime.UtcNow;

        // Workers that are running this model
        private ConcurrentDictionary<int, FoxWorker> _workersRunningModel = new();

        // Lock for model updates
        private static readonly object _modelLock = new();

        // Public properties

        public string Name => _name ?? throw new InvalidOperationException("Name has not been loaded from the database.");
        public bool IsPremium => _isPremium;
        public string? Notes => _notes;
        public string? Description => _description;
        public string? InfoUrl => _infoUrl;
        public string? Category => _category;




        // Populated from settings cache
        public int MaxDimension { get; private set; } = 1024;
        public int DefaultSteps { get; private set; } = FoxSettings.Get<int>("DefaultSteps");
        public decimal DefaultCFG { get; private set; } = FoxSettings.Get<decimal>("DefaultCFGScale");
        public int HiresSteps { get; private set; } = 15;
        public decimal HiresDenoise { get; private set; } = 0.33M;


        enum ModelType
        {
            UNKNOWN, SD15, SDXL, OTHER
        }


        static public async Task<FoxModel> Add(string modelName)
        {
            var model = new FoxModel
            {
                _name = modelName
            };

            if (!_globalModels.TryAdd(modelName, model))
                throw new InvalidOperationException($"Model '{modelName}' already exists.");

            return model;
        }


        static public async Task<FoxModel> GetOrAddFromWorker(FoxWorker worker, string modelName, string sha256Hash)
        {

            FoxModel? model = null;

            if (!TryFind(modelName, out model))
                model = await Add(modelName);

            if (model is null)
                throw new InvalidOperationException("Failed to add or find model.");

            if (model._hash is not null && model._hash != sha256Hash)
                throw new InvalidOperationException($"Hash mismatch for model '{modelName}'. Existing: {model._hash}, New: {sha256Hash}");

            model._workersRunningModel.TryAdd(worker.ID, worker);

            if (model._hash is null)
            {
                model._hash = sha256Hash;

                await model.Save(); // Save the model info to the database
            }

            return model;
        }

        public async Task Save()
        {
            await FoxDB.SaveObjectAsync(this, "model_info");
        }

        public static async Task Initialize()
        {
            var models = await FoxDB.LoadMultipleAsync<FoxModel>("model_info");

            foreach (var model in models)
            {
                if (String.IsNullOrWhiteSpace(model._name))
                    throw new InvalidOperationException("Model name cannot be null or empty.");

                _globalModels[model._name] = model;
            }
        }

        public static bool TryFind(string name, out FoxModel? model)
        {
            return _globalModels.TryGetValue(name, out model);
        }

        public static FoxModel GetOrThrow(string name)
        {
            if (_globalModels.TryGetValue(name, out var model))
                return model;

            throw new KeyNotFoundException($"Model '{name}' not found.");
        }

        public static IEnumerable<FoxModel> Select(Func<FoxModel, bool> predicate)
        {
            return _globalModels.Values.Where(predicate);
        }


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

        public record LimitCheckResult(bool IsAllowed, DenyReason Reason, int dailyLimit, int weeklyLimit)
        {
            public static implicit operator bool(LimitCheckResult result) => result.IsAllowed;

            public bool Has(DenyReason reason) => Reason.HasFlag(reason);
        }

        public async Task<LimitCheckResult> IsUserAllowed(FoxUser user)
        {
            var dailyLimit = 30;
            var weeklyLimit = 200;

            if (!this.IsPremium || user.CheckAccessLevel(AccessLevel.PREMIUM))
                return new(true, DenyReason.None, 0, 0);

            var reason = DenyReason.None;

            var userDailyCount = await GetUserDailyCount(user);
            var userWeeklyCount = await GetUserWeeklyCount(user);

            if (userDailyCount >= dailyLimit)
                reason |= DenyReason.DailyLimitReached;

            if (userWeeklyCount >= weeklyLimit)
                reason |= DenyReason.WeeklyLimitReached;

            return new(reason == DenyReason.None, reason, dailyLimit, weeklyLimit);
        }

        public bool IsAvailable()
        {
            return _enabled && _workersRunningModel.Values.Any(w => w.Online);
        }

        public List<int> GetWorkersRunningModel() => _workersRunningModel.Keys.ToList();

        public static FoxModel? GetModelByName(string modelName) =>
            _globalModels.TryGetValue(modelName, out var model) ? model : null;

        public static List<FoxModel> GetAvailableModels()
        {
            return _globalModels.Values
                .Where(m => m.IsAvailable())
                .ToList();
        }

        public static List<FoxModel> GetAllLoadedModels() => _globalModels.Values.ToList();

        public static List<FoxModel> GetModelsByParameter(Func<FoxModel, bool> filter) =>
            _globalModels.Values.Where(filter).ToList();

        public static void WorkerWentOffline(int workerId)
        {
            // Optional future logic
        }
    }
}
