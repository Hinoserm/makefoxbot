using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxModel
    {
        // Static dictionary to hold all global models by name
        private static Dictionary<string, FoxModel> globalModels = new Dictionary<string, FoxModel>();

        // Model properties
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public string SHA256 { get; private set; }
        public string Title { get; private set; }
        public string FileName { get; private set; }
        public string Config { get; private set; }
        public bool IsPremium { get; private set; }
        public string? Notes { get; private set; }
        public string? Description { get; private set; }

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

            // Add the model to the global model list if it's not already there
            if (!globalModels.ContainsKey(Name))
            {
                globalModels[Name] = this;
            }
        }

        // Add a worker to the model's running workers
        public void AddWorker(int workerId)
        {
            workersRunningModel.Add(workerId);
        }

        // Static method to get or create a FoxModel instance
        public static async Task<FoxModel> GetOrCreateModel(string name, string hash, string sha256, string title, string fileName, string config)
        {
            // If the model exists globally, return it
            if (globalModels.ContainsKey(name))
            {
                return globalModels[name];
            }

            // Otherwise, create a new model
            var newModel = new FoxModel(name, hash, sha256, title, fileName, config);

            // Try to load metadata from the model_info table, if it exists
            await newModel.LoadModelMetadataFromDatabase();

            return newModel;
        }

        // Load additional model metadata from the database (model_info) and update the model properties
        public async Task LoadModelMetadataFromDatabase()
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            string query = @"SELECT is_premium, notes, description
                         FROM model_info 
                         WHERE model_name = @modelName";
            using var cmd = new MySqlCommand(query, SQL);
            cmd.Parameters.AddWithValue("@modelName", Name);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    IsPremium = reader.GetBoolean("is_premium");

                    // Handle nullable fields, assigning null if the value is DBNull
                    Notes = reader.IsDBNull("notes") ? null : reader.GetString("notes");
                    Description = reader.IsDBNull("description") ? null : reader.GetString("description");
                }
                else
                {
                    // If no metadata exists, set defaults
                    IsPremium = false;
                    Notes = null;            
                    Description = null;      
                }
            }
        }

        public static FoxModel? GetModelByName(string modelName)
        {
            globalModels.TryGetValue(modelName, out var model);
            return model;
        }

        public static Dictionary<string, FoxModel> GetAvailableModels()
        {
            // Create a dictionary to store models that are currently loaded by at least 1 worker
            var availableModels = new Dictionary<string, FoxModel>();

            // Iterate over all globally loaded models
            foreach (var model in globalModels.Values)
            {
                // Only add the model to the dictionary if it has at least 1 worker running it
                if (model.workersRunningModel.Any())
                {
                    availableModels[model.Name] = model;
                }
            }

            return availableModels;
        }


        // Get a list of all workers running this model
        public List<int> GetWorkersRunningModel()
        {
            return workersRunningModel.ToList();
        }

        // Static method to get all loaded models globally
        public static List<FoxModel> GetAllLoadedModels()
        {
            return globalModels.Values.ToList();
        }

        // Static method to get all models filtered by a specific parameter (e.g., all premium models)
        public static List<FoxModel> GetModelsByParameter(Func<FoxModel, bool> filter)
        {
            return globalModels.Values.Where(filter).ToList();
        }

        // Static method to handle worker going offline (removes worker from all models)
        public static void WorkerWentOffline(int workerId)
        {
            foreach (var model in globalModels.Values)
            {
                model.workersRunningModel.Remove(workerId);
            }
        }
    }

}
