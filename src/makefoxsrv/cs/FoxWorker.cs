﻿using Autofocus;
using Autofocus.Models;
using MySqlConnector;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using static makefoxsrv.FoxWorker;
using System.Text.RegularExpressions;
using Autofocus.Config;
using WTelegram;
using makefoxsrv;
using TL;
using System.Linq.Expressions;
using Autofocus.Scripts;
using System.Configuration;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace makefoxsrv
{
    public record RegionalPrompter
    : IAdditionalScriptConfig
    {
        public bool Active { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string Mode { get; set; } = "Matrix";
        public string ModeMatrix { get; set; } = "Columns";
        public string ModeMask { get; set; } = "Mask";
        public string ModePrompt { get; set; } = "Prompt";
        public string Ratios { get; set; } = "1";
        public string BaseRatios { get; set; } = "0.3";
        public bool UseBase { get; set; } = false;
        public bool UseCommon { get; set; } = false;
        public bool UseNegCommon { get; set; } = false;
        public string CalcMode { get; set; } = "Latent";
        public bool NotChangeAND { get; set; } = false;
        public string LoRATextEncoder { get; set; } = "0";
        public string LoRAUNet { get; set; } = "0";
        public string Threshold { get; set; } = "0";
        public string Mask { get; set; } = "";
        public string LoRAStopStep { get; set; } = "0";
        public string LoRAHiresStopStep { get; set; } = "0";
        public bool Flip { get; set; } = false;

        public string Key => "Regional Prompter";

        public object ToJsonObject()
        {
            var args = new object[]
            {
            Active,
            Debug,
            Mode,
            ModeMatrix,
            ModeMask,
            ModePrompt,
            Ratios,
            BaseRatios,
            UseBase,
            UseCommon,
            UseNegCommon,
            CalcMode,
            NotChangeAND,
            LoRATextEncoder,
            LoRAUNet,
            Threshold,
            Mask,
            LoRAStopStep,
            LoRAHiresStopStep,
            Flip
            };

            return new { args };
        }
    }

    internal class FoxWorker
    {
        public int ID { get; private set; }

        private CancellationTokenSource stopToken = new(); //Token to stop this worker specifically

        private readonly string address;
        private SemaphoreSlim semaphore = new SemaphoreSlim(0, int.MaxValue);
        private StableDiffusion? api;
        public FoxQueue? qItem = null;   //If we're operating, this is the current queue item being processed.

        public int? MaxImageSize { get; private set; } //width*height.  If null, no limit
        public int? MaxImageSteps { get; private set; } //If null, no limit
        public bool SupportsRegionalPrompter { get; private set; } = false;

        public DateTime StartDate { get; private set; } //Worker start date
        public DateTime? TaskStartDate { get; private set; } = null;
        public DateTime? TaskEndDate { get; private set; } = null;
        public DateTime? LastActivity { get; private set; } = null;

        static public TimeSpan ProgressUpdateInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        private ManualResetEvent enabledEvent = new ManualResetEvent(true); // Initially enabled

        public static event EventHandler<WorkerEventArgs>? OnWorkerStart;
        public static event EventHandler<WorkerEventArgs>? OnWorkerStop;
        public static event EventHandler<WorkerEventArgs>? OnWorkerOnline;
        public static event EventHandler<WorkerEventArgs>? OnWorkerOffline;
        public static event EventHandler<ErrorEventArgs>? OnWorkerError;
        public static event EventHandler<TaskEventArgs>? OnTaskStart;
        public static event EventHandler<TaskErrorEventArgs>? OnTaskError;
        public static event EventHandler<TaskEventArgs>? OnTaskCompleted;
        public static event EventHandler<TaskEventArgs>? OnTaskCancelled;
        public static event EventHandler<ProgressUpdateEventArgs>? OnTaskProgress;

        private bool _online = false;
        public bool Online
        {
            get => _online;
            set
            {
                // Check if the value is actually changing
                if (_online != value)
                {
                    _online = value;

                    try
                    {
                        // If the state is changing to online, raise OnWorkerOnline; otherwise, raise OnWorkerOffline
                        if (_online)
                        {
                            OnWorkerOnline?.Invoke(this, new WorkerEventArgs());
                        }
                        else
                        {
                            FoxModel.WorkerWentOffline(this.ID);
                            OnWorkerOffline?.Invoke(this, new WorkerEventArgs());
                        }
                    }
                    catch { }
                }
            }
        }

        public bool Enabled
        {
            get => !enabledEvent.WaitOne(0); // If it waits 0 ms, it's checking the current state
            set
            {
                if (value)
                    enabledEvent.Set(); // Enable work
                else
                    enabledEvent.Reset(); // Disable work, causing the thread to block
            }
        }

        public string name;

        private static ConcurrentDictionary<int, FoxWorker> workers = new ConcurrentDictionary<int, FoxWorker>();

        public IProgress? Progress = null;

        public string? LastUsedModel { get; private set; } = null;

        //public Dictionary<string, int> availableModels { get; private set; } = new Dictionary<string, int>();

        //private LinkedList<string>? recentModels = new LinkedList<string>();
        //private Dictionary<string, LinkedListNode<string>>? modelNodes = new Dictionary<string, LinkedListNode<string>>();
        //private int _modelCapacity;

        //public int ModelCapacity
        //{
        //    get => _modelCapacity;
        //    set
        //    {
        //        _modelCapacity = value;

        //        if (_modelCapacity == 0)
        //        {
        //            // Clear tracking structures if capacity is set to 0.
        //            if (recentModels is not null)
        //                recentModels?.Clear();
        //            if (modelNodes is not null)
        //                modelNodes?.Clear();

        //            recentModels = null;
        //            modelNodes = null;
        //        }
        //        else
        //        {
        //            // Ensure structures are initialized (they are by default, but this is for clarity and future-proofing).
        //            if (recentModels is null)
        //                recentModels = new LinkedList<string>();
        //            if (modelNodes is null)
        //                modelNodes = new Dictionary<string, LinkedListNode<string>>();

        //            // If increasing capacity from 0, no need to trim structures, as they were just cleared or are already initialized.
        //            // Only trim if the count exceeds the new, non-zero capacity.
        //            while (recentModels.Count > _modelCapacity)
        //            {
        //                var oldestModel = recentModels.First?.Value;
        //                if (oldestModel is not null)
        //                {
        //                    recentModels.RemoveFirst();
        //                    modelNodes.Remove(oldestModel);
        //                }
        //            }
        //        }
        //    }
        //}

        //public void UseModel(string model)
        //{
        //    if (string.IsNullOrEmpty(model) || _modelCapacity == 0)
        //        return; // Do not track if capacity is 0 or model is invalid.

        //    if (modelNodes is null || recentModels is null)
        //        return; // Do not track if structures are not initialized.

        //    //FoxLog.WriteLine($"Worker {ID} - Using model: {model}");

        //    if (modelNodes.TryGetValue(model, out var existingNode))
        //    {
        //        // Update the model's position to the end as the most recently used.
        //        recentModels.Remove(existingNode);
        //        recentModels.AddLast(existingNode);
        //    }
        //    else
        //    {
        //        // Add a new model, ensuring capacity constraints are respected.
        //        if (recentModels.Count >= ModelCapacity)
        //        {
        //            var oldestModel = recentModels.First?.Value;
        //            if (oldestModel is not null) {
        //                recentModels.RemoveFirst();
        //                modelNodes.Remove(oldestModel);
        //            }
        //        }

        //        var newNode = new LinkedListNode<string>(model);
        //        recentModels.AddLast(newNode);
        //        modelNodes[model] = newNode;
        //    }
        //}

        //public IEnumerable<string> GetRecentModels() => (recentModels is not null) ? recentModels.AsEnumerable() : Enumerable.Empty<string>();

        private class Lora
        {
            public string Name { get; set; }
            public string Alias { get; set; }
            public string Path { get; set; }
            public JsonElement Metadata { get; set; }
        }

        // Constructor to initialize the botClient and address
        private FoxWorker(int worker_id, string address, string name, CancellationToken cancellationToken)
        {
            this.stopToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            this.address = address;
            this.ID = worker_id;
            this.name = name;
        }

        private static FoxWorker CreateWorker(int worker_id, string address, string name, CancellationToken cancellationToken)
        {
            var worker = new FoxWorker(worker_id, address, name, cancellationToken);

            bool added = workers.TryAdd(worker_id, worker); // Store the worker instance
            if (!added)
                throw new Exception("Unable to add worker");

            return worker;
        }


        public static async Task<String?> GetWorkerName(int? worker_id)
        {
            if (worker_id is null)
                return null;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();


            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = "SELECT name FROM workers WHERE id = @id";
                cmd.Parameters.AddWithValue("id", worker_id);
                var result = await cmd.ExecuteScalarAsync();

                if (result is not null && result is not DBNull)
                    return Convert.ToString(result);
            }

            return null;
        }

        public static async Task LoadWorkers(CancellationToken cancellationToken)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync(cancellationToken);

            MySqlCommand cmd = new MySqlCommand("SELECT * FROM workers WHERE enabled > 0", SQL);

            using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                if (!reader.HasRows)
                    throw new Exception("No workers available in the database.");

                while (await reader.ReadAsync(cancellationToken))
                {
                    int id = reader.GetInt32("id");
                    string url = reader.GetString("url");
                    string name = reader.GetString("name");

                    FoxLog.WriteLine($"Loading worker {id} - {url}");

                    var worker = CreateWorker(id, url, name, cancellationToken);


                    if (!(reader["max_img_size"] is DBNull))
                        worker.MaxImageSize = reader.GetInt32("max_img_size");

                    if (!(reader["max_img_steps"] is DBNull))
                        worker.MaxImageSteps = reader.GetInt32("max_img_steps");

                    if (!(reader["regional_prompting"] is DBNull))
                        worker.SupportsRegionalPrompter = reader.GetBoolean("regional_prompting");

                    await worker.SetStartDate();

                    try
                    {
                        await worker.LoadModelInfo();
                        //await worker.GetLoRAInfo();
                        await worker.SetOnlineStatus(true);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex);
                    }

                    //_ = worker.Run(botClient);
                    //_ = Task.Run(async () => await worker.Run());
                }
            }
        }

        public static async Task StartWorkers()
        {
            if (workers.Count() < 1)
                    throw new Exception("No workers available.");

            foreach (var worker in workers.Values)
            {

                _ = worker.Start();
                //_ = Task.Run(async () => await worker.Start());

                FoxLog.WriteLine($"Worker {worker.ID} - Started.");
            }
        }

        public static async Task StopWorkers(bool waitForStopped = false)
        {
            foreach (var worker in workers.Values)
            {
                worker.Stop();
            }

            if (!waitForStopped)
                return;

            await Task.WhenAll(workers.Values.Select(w =>
            {
                var tcs = new TaskCompletionSource<bool>();
                ThreadPool.RegisterWaitForSingleObject(
                    w.stopToken.Token.WaitHandle,
                    (state, timedOut) => ((TaskCompletionSource<bool>)state!).SetResult(true),
                    tcs,
                    Timeout.Infinite,
                    true
                );
                return tcs.Task;
            }));
        }

        public static FoxWorker? Get(int workerId)
        {
            workers.TryGetValue(workerId, out FoxWorker? worker);

            return worker;
        }

        public static ConcurrentDictionary<int, FoxWorker> GetWorkers()
        {
            return workers;
        }

        public void Stop()
        {
            FoxLog.WriteLine($"Worker {ID} stopping due to request... ");

            stopToken.Cancel();
        }

        private async Task<StableDiffusion?> ConnectAPI(bool throw_error = true)
        {
            try
            {
                if (this.api is null)
                {
                    this.api = new StableDiffusion(address)
                    {
                        TimeoutSlow = TimeSpan.FromMinutes(3)
                    };
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.stopToken.Token);

                await this.api.Ping(cts.Token);

                return api;
            } catch (Exception ex)
            {
                await SetFailedDate(ex);

                if (throw_error)
                    throw;
            }

            return null;
        }

        public async Task LoadModelInfo()
        {
            long modelCount = 0;
            var api = await ConnectAPI();

            if (api is null)
                throw new Exception("Unable to connect to host.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.stopToken.Token);

            // Retrieve models from the worker's API
            var models = await api.StableDiffusionModels(cts.Token);

            foreach (var modelData in models)
            {
                // Try to get or create the model globally
                var foxModel = await FoxModel.GetOrCreateModel(
                    modelData.ModelName,
                    modelData.Hash,
                    modelData.SHA256,
                    modelData.Title,
                    modelData.FileName,
                    modelData.Config
                );

                // Check if the hashes match, if not log a warning
                if (foxModel.Hash != modelData.Hash || foxModel.SHA256 != modelData.SHA256)
                {
                    FoxLog.WriteLine($"Warning: Model '{modelData.ModelName}' on worker {this.ID} has mismatched hashes.");
                }

                // Add this worker to the model in memory (whether or not the hashes match)
                foxModel.AddWorker(this.ID);
                modelCount++;
            }

            FoxLog.WriteLine($"Worker {this.ID} - Loaded {modelCount} available models.");
        }


        public async Task GetLoRAInfo()
        {
            long lora_count = 0;
            long lora_tag_count = 0;

            using var httpClient = new HttpClient();
            var apiUrl = address + "/sdapi/v1/loras";

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using var transaction = SQL.BeginTransaction();

            try
            {
                var response = await httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                var jsonString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loras = JsonSerializer.Deserialize<List<Lora>>(jsonString, options);

                // Fetch existing LoRAs for the worker to identify deletions
                var lorasToKeep = new HashSet<string>(loras.Select(l => l.Name));
                var loraNames = string.Join(",", lorasToKeep.Select(n => $"'{n.Replace("'", "''")}'"));

                var cmd = new MySqlCommand($"DELETE FROM worker_loras WHERE worker_id = @workerId AND name NOT IN ({loraNames})", SQL, transaction);
                cmd.Parameters.AddWithValue("@workerId", ID);

                await cmd.ExecuteNonQueryAsync();


                foreach (var lora in loras)
                {
                    // Attempt to insert, or update if exists
                    var insertOrUpdateCmd = new MySqlCommand(@"
                        INSERT INTO worker_loras (worker_id, name, alias, path)
                        VALUES (@workerId, @name, @alias, @path)
                        ON DUPLICATE KEY UPDATE alias=VALUES(alias), path=VALUES(path);", SQL, transaction);
                    insertOrUpdateCmd.Parameters.AddWithValue("@workerId", ID);
                    insertOrUpdateCmd.Parameters.AddWithValue("@name", lora.Name);
                    insertOrUpdateCmd.Parameters.AddWithValue("@alias", lora.Alias ?? "");
                    insertOrUpdateCmd.Parameters.AddWithValue("@path", lora.Path);
                    await insertOrUpdateCmd.ExecuteNonQueryAsync();

                    // Retrieve the lora_id
                    var getLoraIdCmd = new MySqlCommand(@"
                        SELECT lora_id FROM worker_loras
                        WHERE worker_id = @workerId AND name = @name;", SQL, transaction);
                    getLoraIdCmd.Parameters.AddWithValue("@workerId", ID);
                    getLoraIdCmd.Parameters.AddWithValue("@name", lora.Name);
                    var loraId = Convert.ToInt64(await getLoraIdCmd.ExecuteScalarAsync());

                    var deleteOldTagCmd = new MySqlCommand($"DELETE FROM worker_lora_tags WHERE lora_id = @id", SQL, transaction);
                    deleteOldTagCmd.Parameters.AddWithValue("@id", loraId);

                    await deleteOldTagCmd.ExecuteNonQueryAsync();

                    lora_count++;

                    Dictionary<string, int> tagFrequencies = new Dictionary<string, int>();

                    if (lora.Metadata.TryGetProperty("ss_tag_frequency", out var ssTagFrequency) && ssTagFrequency.ValueKind == JsonValueKind.Object)
                    {
                        // Iterate over each category in the ss_tag_frequency
                        foreach (var category in ssTagFrequency.EnumerateObject())
                        {
                            if (category.Value.ValueKind == JsonValueKind.Object)
                            {
                                // Iterate over each tag within the category
                                foreach (var tag in category.Value.EnumerateObject())
                                {
                                    // Check if the value is a string or an integer
                                    if (tag.Value.ValueKind == JsonValueKind.Number)
                                    {
                                        // Add the tag name and count to the dictionary
                                        tagFrequencies[tag.Name] = tag.Value.GetInt32();
                                    }
                                    //else if (tag.Value.ValueKind == JsonValueKind.String)
                                    //{
                                    //    // If it's a string, enter it with a value of 1
                                    //    tagFrequencies[tag.Name] = 1;
                                    //}
                                }
                            }
                            else if (category.Value.ValueKind == JsonValueKind.String)
                            {
                                // Handle the case where the entire category is a string
                                //tagFrequencies[category.Name] = 1;
                            }
                        }
                    }
                    else if (ssTagFrequency.ValueKind == JsonValueKind.String)
                    {
                        // Handle the case where ss_tag_frequency itself is a string
                        string tagName = ssTagFrequency.GetString();
                        if (!string.IsNullOrEmpty(tagName))
                        {
                            //tagFrequencies[tagName] = 1;
                        }
                    }

                    lora_tag_count += tagFrequencies.Count();

                    // Start a transaction

                    var values = tagFrequencies.Select(tag => $"({loraId}, {ID}, '{tag.Key.Replace("'", "''")}', {tag.Value})");
                    var insertTagsCmdText = $"INSERT INTO worker_lora_tags (lora_id, worker_id, tag_name, frequency) VALUES {string.Join(", ", values)} ON DUPLICATE KEY UPDATE frequency=VALUES(frequency);";
                    if (values.Any())
                    {
                        var insertTagsCmd = new MySqlCommand(insertTagsCmdText, SQL, transaction);
                        await insertTagsCmd.ExecuteNonQueryAsync();
                    }
                }

                // Commit the transaction
                await transaction.CommitAsync();

                FoxLog.WriteLine($"  Worker {ID} - Loaded {lora_count} LoRAs with {lora_tag_count} tags.");
            }
            catch (HttpRequestException ex)
            {
                FoxLog.LogException(ex, $"Error fetching LoRAs: {ex.Message}");

                await transaction.RollbackAsync();
            }
        }


        public async Task LoadEmbeddingInfo()
        {
            var api = await ConnectAPI();

            if (api is null)
                throw new Exception("Unable to connect to host.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.stopToken.Token);

            var embeddings = await api.Embeddings(cts.Token); // Get embeddings instead of models

            FoxLog.WriteLine("Embeddings Information:");
            foreach (var embedding in embeddings.All)
            {
                FoxLog.WriteLine($"Name: {embedding.Value.Name}");
                FoxLog.WriteLine($"Step: {embedding.Value.Step}");
                FoxLog.WriteLine($"Checkpoint: {embedding.Value.Checkpoint}");
                FoxLog.WriteLine($"Checkpoint Name: {embedding.Value.CheckpointName}");
                FoxLog.WriteLine($"Shape: {embedding.Value.Shape}");
                FoxLog.WriteLine($"Vectors: {embedding.Value.Vectors}");
                FoxLog.WriteLine("----------");
            }
        }

        //public static Task<Dictionary<string, List<int>>> GetAvailableModels()
        //{
        //    var models = new Dictionary<string, List<int>>();

        //    // Iterate over all global models
        //    foreach (var model in FoxModel.GetAllLoadedModels())
        //    {
        //        // Get the workers running this model
        //        var workers = model.GetWorkersRunningModel();
        //        if (workers.Any())
        //        {
        //            models[model.Name] = workers;
        //        }
        //    }

        //    return Task.FromResult(models);
        //}

        //public static Task<List<int>?> GetWorkersForModel(string modelName)
        //{
        //    // Try to get the model from the global list
        //    var model = FoxModel.GetAllLoadedModels().FirstOrDefault(m => m.Name == modelName);

        //    // If the model doesn't exist or has no workers, return null
        //    if (model == null || !model.GetWorkersRunningModel().Any())
        //    {
        //        return Task.FromResult<List<int>?>(null);
        //    }

        //    // Otherwise, return the list of workers running this model
        //    return Task.FromResult<List<int>?>(model.GetWorkersRunningModel());
        //}


        public static ICollection<int> GetActiveWorkerIds()
        {
            return workers.Keys;
        }

        public void Ping(int count = 1)
        {
            semaphore.Release(count);
        }

        private static void StopWorker(int workerId)
        {
            if (workers.TryRemove(workerId, out FoxWorker? worker))
            {
                if (worker is not null)
                    worker.stopToken.Cancel();

                // Perform any additional cleanup if necessary
            }
        }

        private async Task SetUsedDate()
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET last_queue_id = @queue_id, date_used = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("queue_id", this.qItem?.ID);
                cmd.Parameters.AddWithValue("id", ID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetStartDate()
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET date_started = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", ID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetFailedDate(Exception ex)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET online = 0, last_error = @error, date_failed = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("error", ex.Message);
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", ID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetOnlineStatus(bool status = true)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET date_used = @now, online = @status WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("status", status);
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", ID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task HandleError(Exception ex)
        {
            Online = false;
            FoxLog.WriteLine($"Worker {ID} is offline!\r\n  Error: {ex.Message}\r\n{ex.StackTrace}");
            //await SetOnlineStatus(false); //SetFailedDate() already marks us as offline.
            await SetFailedDate(ex);

            //If we have the semaphore and crash, we better give it to someone else.
            //if (semaphoreAcquired)
            //    semaphore.Release();

            //semaphoreAcquired = false;

            if (qItem is not null)
            {
                await qItem.SetError(ex);
                try
                {
                    OnTaskError?.Invoke(this, new TaskErrorEventArgs(qItem, ex));
                } catch (Exception ex2) {
                    FoxLog.LogException(ex2, $"Error running OnTaskError: {ex2.Message}");
                }
            }

            try
            {
                OnWorkerError?.Invoke(this, new ErrorEventArgs(ex));
            }
            catch (Exception ex2)
            {
                FoxLog.LogException(ex2, $"Error running OnWorkerError: {ex2.Message}");
            }

            if (qItem is not null)
            {
                qItem = null;
                TaskStartDate = null;
                TaskEndDate = null;
            }
        }

        public bool AssignTask(FoxQueue q)
        {
            if (!Online)
                return false; //Can't accept work if we're offline.

            if (qItem is not null)
                return false; //Already working on something.

            qItem = q;
            Ping();

            return true;
        }

        private CancellationTokenSource StartProgressMonitor(FoxQueue qItem, CancellationToken cancellationToken, double manualScale = 1, double startScale = 0)
        {   
            CancellationTokenSource progressCTS = new CancellationTokenSource();

            if (api is null)
                return progressCTS;

            _= Task.Run(async () =>
            {
                double lastProgress = 0;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressCTS.Token);

                while (!cts.IsCancellationRequested && Online && qItem is not null)
                {
                    try
                    {
                        IProgress p = await api.Progress(true, cts.Token);

                        double progress = (p.Progress / manualScale) + startScale;

                        if (progress > lastProgress)
                        {
                            if (!Online)
                                break;

                            if (qItem is null)
                                break;

                            lastProgress = progress;

                            float progressPercent = (float)Math.Round(progress * 100, 2);

                            if (qItem is not null)
                            {
                                if (qItem.IsFinished()) // Cancelled or finished prematurely, shut down.
                                    break;

                                OnTaskProgress?.Invoke(this, new ProgressUpdateEventArgs(qItem, progress));
                                _= qItem.Progress(this, progressPercent);
                            }
                            else
                                break;

                            if (p.Progress >= 1.0)
                                break; //Stop when we hit 100%.
                        }
                        else if (lastProgress > 0 && p.Progress <= 0)
                            break;

                        await Task.Delay(ProgressUpdateInterval, cts.Token);
                    }
                    catch(Exception ex) when (ex is not OperationCanceledException)
                    {
                        FoxLog.LogException(ex, $"Progress monitor error: {ex.Message}");
                        break; //API error, stop the loop.
                    }
                }

                this.Progress = null; //Set it back to null when finished.
            });

            return progressCTS;
        }

        private async Task Start()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken.Token);

            //var waitHandles = new WaitHandle[] { enabledEvent, cts.Token.WaitHandle };

            try
            {
                OnWorkerStart?.Invoke(this, new WorkerEventArgs());
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Error running OnWorkerStart: {ex.Message}");

            }

            _ = Task.Run(async () =>
            {

                // Initialize FoxContext for this worker
                FoxContextManager.Current = new FoxContext
                {
                    Worker = this
                };

                try
                {
                    Online = await ConnectAPI(false) is not null;

                    while (!cts.IsCancellationRequested)
                    {
                        var waitMs = 6000; //Default wait time

                        try
                        {
                            var semaphoreAquired = await semaphore.WaitAsync(waitMs, cts.Token);

                            //FoxLog.WriteLine($"Worker {ID} - Aquired semaphore: {semaphoreAquired}, Online: {Online}, Task: {qItem?.ID.ToString() ?? "None"}", LogLevel.DEBUG);

                            //WaitHandle.WaitAny(waitHandles);

                            api = await ConnectAPI(false);

                            var newOnlineStatus = api is not null && FoxTelegram.IsConnected;

                            if (Online != newOnlineStatus)
                            {
                                if (newOnlineStatus)
                                {
                                    //Coming back online
                                    await Task.Delay(8000, cts.Token);

                                    FoxLog.WriteLine($"Worker {ID} is back online!");
                                    await SetOnlineStatus(true);

                                    await LoadModelInfo(); //Reload in case it changed.

                                    Online = true;
                                    waitMs = 2000;
                                }
                                else
                                {
                                    //Going offline

                                    await HandleError(new Exception("Could not connect to server"));
                                    waitMs = 10000; //Wait longer if we're offline.
                                }
                            }

                            if (Online)
                            {
                                while ((api = await ConnectAPI(true)) is not null)
                                {
                                    //If we're not the one currently processing...
                                    var status = await api.QueueStatus();
                                    var progress = await api.Progress(true);

                                    if ((status.QueueSize > 0 || progress.State.JobCount > 0))
                                    {
                                        //Online = false;
                                        var busyWaitTime = (status.QueueSize + progress.State.JobCount) * 1500;

                                        //FoxLog.WriteLine($"Worker {ID} - Busy. Waiting {busyWaitTime}ms.", LogLevel.DEBUG);

                                        await Task.Delay(busyWaitTime, cts.Token);

                                        continue;
                                    }
                                    else
                                    {
                                        //Online = true;
                                        break;
                                    }
                                }
                            }

                            if (Online && api is not null && qItem is not null && FoxTelegram.IsConnected)
                            {
                                
                                this.LastActivity = DateTime.Now;
                                await ProcessTask(cts.Token);

                                if (qItem is not null)
                                {
                                    //FoxLog.WriteLine($"Processing completed, but qItem is not null!");
                                    qItem = null;
                                }
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            if (qItem is not null)
                                await qItem.SetError(ex);

                            break; //Break the loop for a graceful shutdown
                        }
                        catch (Exception ex)
                        {
                            await HandleError(ex);
                        }
                    }

                    this.Online = false;

                    try
                    {
                        OnWorkerStop?.Invoke(this, new WorkerEventArgs());
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex, $"Error running OnWorkerStop: {ex.Message}");
                    }

                    FoxLog.WriteLine($"Worker {ID} - Shutdown.");
                    workers.TryRemove(ID, out _);
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
                }
                finally
                {
                    // Clean up the FoxContext
                    FoxContextManager.Clear();
                }
            });
        }

        public async Task ProcessTask(CancellationToken ctToken)
        {
            CancellationTokenSource? progressCTS = null;

            try
            {
                FoxContextManager.Current.Queue = qItem;
                FoxContextManager.Current.Worker = this;

                FoxLog.WriteLine($"Worker {ID} - Start processing task {qItem.ID}...", LogLevel.DEBUG);

                if (qItem is null)
                    throw new Exception("Attempt to process task when no task was assigned");

                FoxContextManager.Current.User = qItem?.User;
                FoxContextManager.Current.Telegram = qItem?.Telegram;
                FoxContextManager.Current.Message = new Message { id = qItem.MessageID };

                if (!FoxTelegram.IsConnected)
                    throw new Exception("Telegram client is disconnected.");

                FoxLog.WriteLine($"Worker {this.name} is now processing task {qItem.ID}", LogLevel.DEBUG);

                if (api is null)
                    throw new Exception("API not available (Should have been loaded before we got here)");

                this.TaskStartDate = DateTime.Now;
                this.TaskEndDate = null;

                await qItem.SetWorker(this);

                await this.SetUsedDate();

                var ctsLoop = CancellationTokenSource.CreateLinkedTokenSource(ctToken, qItem.stopToken.Token);

                OnTaskStart?.Invoke(this, new TaskEventArgs(qItem));
                await qItem.Start(this);

                var settings = qItem.Settings.Copy();

                if (LastUsedModel != settings.model)
                    FoxLog.WriteLine($"Switching model from {LastUsedModel ?? "(none)"} to {settings.model}", LogLevel.DEBUG);

                this.LastUsedModel = settings.model;

                Byte[] outputImage;

                FoxQueue.QueueType generationType = qItem.Type;

                if (generationType == FoxQueue.QueueType.IMG2IMG)
                {
                    

                    FoxImage? inputImage = await qItem.GetInputImage();

                    if (inputImage is null || inputImage.Image is null)
                        throw new Exception("Input image could not be loaded.");

                    if (qItem.User is null)
                        throw new Exception("User is null.");

                    var img = inputImage.Image;
                    byte[]? maskImage = null;

                    var maskSteps = qItem.User.CheckAccessLevel(AccessLevel.PREMIUM) ? 15 : 5;

                    (maskImage, img) = GenerateMask(inputImage, settings.width, settings.height);

                    progressCTS = StartProgressMonitor(
                        qItem: qItem, 
                        cancellationToken: ctsLoop.Token,
                        manualScale: maskImage is null ? 1 : 2,
                        startScale: 0
                    );

                    if (maskImage is not null)
                    {
                        FoxLog.WriteLine($"Using mask.");

                        img = await RunImg2Img(
                            qItem: qItem,
                            inputImage: img,
                            maskImage: maskImage,
                            steps: maskSteps,
                            denoisingStrength: 0.70,
                            hiresEnabled: false,
                            invertMask: false,
                            cancellationToken: ctsLoop.Token
                        );

                        if (progressCTS is not null)
                            progressCTS.Cancel();

                        progressCTS = StartProgressMonitor(
                            qItem: qItem,
                            cancellationToken: ctsLoop.Token,
                            manualScale: 2,
                            startScale: 0.5
                        );
                    }

                    outputImage = await RunImg2Img(
                        qItem: qItem,
                        inputImage: img,
                        cancellationToken: ctsLoop.Token
                    );
                }
                else if (generationType == FoxQueue.QueueType.TXT2IMG)
                {
                    progressCTS = StartProgressMonitor(qItem, ctsLoop.Token);

                    //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                    //var model = await api.StableDiffusionModel("indigoFurryMix_v90Hybrid");
                    var model = await api.StableDiffusionModel(settings.model, ctsLoop.Token);
                    //var sampler = await api.Sampler("DPM++ 2M Karras", ctsLoop.Token);
                    //var sampler = await api.Sampler("Restart", ctsLoop.Token);
                    //var sampler = await api.Sampler(settings.model == "redwater_703" ? "DPM++ 2M Karras" : "Euler A", ctsLoop.Token);
                    var sampler = await api.Sampler(settings.sampler);

                    var width = settings.width;
                    var height = settings.height;

                    HighResConfig? hiResConfig = null;

                    if (settings.hires_enabled)
                    {
                        var upscaler = await api.Upscaler("4x_foolhardy_Remacri", ctsLoop.Token);
                        hiResConfig = new HighResConfig()
                        {
                            Width = settings.hires_width,
                            Height = settings.hires_height,
                            DenoisingStrength = (double)settings.hires_denoising_strength,
                            Upscaler = upscaler,
                            Steps = settings.hires_steps
                        };
                    }

                    var config = new TextToImageConfig()
                    {
                        Model = model,

                        Prompt = new()
                        {
                            Positive = settings.prompt,
                            Negative = settings.negative_prompt,
                        },

                        Seed = new()
                        {
                            Seed = settings.seed,
                            SubSeed = settings.variation_seed,
                            SubseedStrength = (double?)settings.variation_strength,
                        },

                        Width = width,
                        Height = height,

                        Sampler = new()
                        {
                            Sampler = sampler,
                            SamplingSteps = settings.steps,
                            CfgScale = (double)settings.cfgscale
                        },
                        HighRes = hiResConfig
                    };

                    if (qItem.RegionalPrompting)
                    {
                        FoxLog.WriteLine($"Worker {ID} (Task {qItem?.ID.ToString() ?? "[unknown]"}) - Using regional prompting extension.", LogLevel.DEBUG);

                        // Add Regional Prompter configuration
                        config.AdditionalScripts.Add(new RegionalPrompter()); // Use default options.
                    }

                    var txt2img = await api.TextToImage(config, ctsLoop.Token);

                    outputImage = txt2img.Images.First().Data.ToArray();
                }
                else
                    throw new NotImplementedException("Request type not implemented.");

                if (progressCTS is not null)
                    progressCTS.Cancel();

                try {
                    if (qItem is null)
                        throw new Exception("Task was null after processing.");

                    if (qItem.stopToken.IsCancellationRequested)
                        throw new OperationCanceledException("User cancelled request.");

                    this.TaskStartDate = DateTime.Now;
                    await qItem.Send(outputImage);
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
                }

                try
                {
                    OnTaskCompleted?.Invoke(this, new TaskEventArgs(qItem));
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, $"Error while running OnTaskCompleted: {ex.Message}");
                }
            }
            catch (SDHttpException ex)
            {
                //We probably don't need to crash the whole worker for these.
                try
                {
                    await qItem.SetError(ex);
                    OnTaskError?.Invoke(this, new TaskErrorEventArgs(qItem, ex));
                }
                catch (Exception ex2)
                {
                    FoxLog.LogException(ex2, $"Error running OnTaskError: {ex2.Message}");
                }
            }
            catch (WTelegram.WTException ex)
            {
                //If we can't edit, we probably hit a rate limit with this user.

                if (ex is RpcException rex)
                {
                    if ((ex.Message.EndsWith("_WAIT_X") || ex.Message.EndsWith("_DELAY_X")))
                    {
                        // If the message matches, extract the number
                        int retryAfterSeconds = rex.X;
                        //FoxLog.WriteLine($"Worker {ID} - Queue ID {qItem?.ID} User {qItem?.User?.UID} - Rate limit exceeded. Trying again after {retryAfterSeconds} seconds.");

                        FoxLog.LogException(ex, $"Rate limit exceeded (X={rex.X}): {ex.Message}");

                        if (qItem is not null)
                        {
                            try
                            {
                                await qItem.SetError(ex);
                                OnTaskError?.Invoke(this, new TaskErrorEventArgs(qItem, ex));
                            } catch (Exception ex2)
                            {
                                FoxLog.LogException(ex2, $"Error running OnTaskError: {ex2.Message}");
                            }
                            //_ = FoxQueue.Enqueue(qItem);
                        }
                    }
                    else if (ex.Message == "INPUT_USER_DEACTIVATED")
                    {
                        //FoxLog.WriteLine($"Worker {ID} - User deactivated. Stopping task.");
                        FoxLog.LogException(ex, $"User deactivated. Stopping task.  Error: {ex.Message}");

                        if (qItem is not null)
                        {
                            try
                            {
                                await qItem.SetCancelled();
                                OnTaskCancelled?.Invoke(this, new TaskEventArgs(qItem));
                            } catch (Exception ex2)
                            {
                                FoxLog.LogException(ex2, $"Error running OnTaskCancelled: {ex2.Message}");
                            }
                        }
                    }
                    else
                    {
                        FoxLog.LogException(ex, $"Telegram Error (X={rex.X}): {ex.Message}");
                        await HandleError(ex);
                    }
                }
                else //We don't care about other telegram errors, but log them for debugging purposes.
                    FoxLog.LogException(ex, $"Telegram Error: {ex.Message}");
            }
            catch (OperationCanceledException ex)
            {
                // Handle the cancellation specifically
                if (qItem is not null && qItem.stopToken.IsCancellationRequested)
                {
                    FoxLog.WriteLine($"Worker {ID} - User Cancelled");

                    try
                    {
                        await qItem.SetCancelled();

                        using var httpClient = new HttpClient();

                        // Construct the final URL
                        var finalUrl = new Uri(new Uri(address), "/sdapi/v1/interrupt");

                        // Make the HTTP POST request
                        var response = await httpClient.PostAsync(finalUrl, null, stopToken.Token);
                        response.EnsureSuccessStatusCode();

                        OnTaskCancelled?.Invoke(this, new TaskEventArgs(qItem));
                    }
                    catch (Exception ex2)
                    {
                        FoxLog.LogException(ex, $"Error running OnTaskCancelled: {ex2.Message}");
                    }
                    finally
                    {
                        qItem = null;
                    }
                }
                else if (this.stopToken.IsCancellationRequested)
                {
                    // Graceful shutdown of the worker.
                    if (qItem is not null)
                        await qItem.SetError(ex);

                    throw;
                } else
                    await HandleError(ex);
            }
            finally
            {
                if (progressCTS is not null)
                    progressCTS.Cancel();

                FoxLog.WriteLine($"Worker {ID} - Task completed.", LogLevel.DEBUG);
            }
        }

        private async Task<byte[]> RunImg2Img(FoxQueue qItem, byte[] inputImage, CancellationToken cancellationToken,
                                              byte[]? maskImage = null, bool invertMask = false, int? steps = null,
                                              double? denoisingStrength = null, int? seed = null, bool? hiresEnabled = null)
        {
            var settings = qItem.Settings.Copy();

            if (hiresEnabled is null)
                hiresEnabled = settings.hires_enabled;

            if (steps is null)
                steps = hiresEnabled.Value ? (int)settings.hires_steps : settings.steps;

            if (denoisingStrength is null)
                denoisingStrength = hiresEnabled.Value ? (double)settings.hires_denoising_strength : (double)settings.denoising_strength;

            if (this.api is null)
                throw new Exception("API not currently available");

            var model = await api.StableDiffusionModel(settings.model, cancellationToken);
            var sampler = await api.Sampler(settings.sampler);

            var img = new Base64EncodedImage(inputImage);

            var config = new ImageToImageConfig()
            {
                Images = { img },

                Model = model,

                Prompt = new()
                {
                    Positive = settings.prompt ?? "",
                    Negative = settings.negative_prompt ?? "",
                },

                Mask = maskImage is null ? null : new Base64EncodedImage(maskImage),
                InpaintingMaskInvert = invertMask,
                MaskBlur = 0,

                Width = hiresEnabled.Value ? settings.hires_width : settings.width,
                Height = hiresEnabled.Value ? settings.hires_height : settings.height,

                Seed = new()
                {
                    Seed = seed ?? settings.seed,
                    SubSeed = settings.variation_seed,
                    SubseedStrength = (double?)settings.variation_strength,
                },
                ResizeMode = 2,
                InpaintingFill = MaskFillMode.Fill,
                DenoisingStrength = denoisingStrength ?? (double)settings.denoising_strength,

                Sampler = new()
                {
                    Sampler = sampler,
                    SamplingSteps = steps ?? settings.steps,
                    CfgScale = (double)settings.cfgscale
                }
            };

            if (qItem.RegionalPrompting)
            {
                FoxLog.WriteLine($"Worker {ID} (Task {qItem?.ID.ToString() ?? "[unknown]"}) - Using regional prompting extension.", LogLevel.DEBUG);

                // Add Regional Prompter configuration
                config.AdditionalScripts.Add(new RegionalPrompter()); // Use default options.;
            }

            var img2img = await api.Image2Image(config, cancellationToken);

            return img2img.Images.First().Data.ToArray();
        
        }

        private (byte[]?, byte[]) GenerateMask(FoxImage inputImage, uint width, uint height)
        {
            if (inputImage is null || inputImage.Image is null)
                throw new ArgumentNullException("Input image is NULL.");

            byte[]? maskImage = null;
            byte[] resizedInputImage = inputImage.Image;

            int inputWidth = inputImage.Width;
            int inputHeight = inputImage.Height;

            if (width == inputWidth && height == inputHeight)
                return (null, inputImage.Image); // No need to generate a mask if the dimensions match

            if (inputImage != null)
            {
                var requestedAspect = (float)width / (float)height;
                var inputAspect = (float)inputWidth / (float)inputHeight;

                int scaledW, scaledH;

                // Determine scaled dimensions based on the input image size
                if (inputAspect > requestedAspect)
                {
                    // Input image is wider than requested aspect
                    scaledW = inputWidth;
                    scaledH = (int)Math.Round(scaledW / requestedAspect);
                }
                else
                {
                    // Input image is taller than requested aspect
                    scaledH = inputHeight;
                    scaledW = (int)Math.Round(scaledH * requestedAspect);
                }

                if (scaledW == inputWidth && scaledH == inputHeight)
                    return (null, inputImage.Image); // No need to generate a mask if the dimensions match

                //FoxLog.WriteLine($"Requested size: {width}x{height}");
                //FoxLog.WriteLine($"Image size: {inputWidth}x{inputHeight}");
                //FoxLog.WriteLine($"Scaled size: {scaledW}x{scaledH}");

                int offsetX = (scaledW - inputWidth) / 2;
                int offsetY = (scaledH - inputHeight) / 2;

                // Create mask
                using (var maskImageSharp = new Image<Rgba32>(scaledW, scaledH))
                {
                    maskImageSharp.Mutate(ctx =>
                    {
                        // Fill the entire mask with black (padding)
                        ctx.Fill(SixLabors.ImageSharp.Color.White);

                        // Add a white rectangle for the resized content area
                        ctx.Fill(
                            SixLabors.ImageSharp.Color.Black,
                            new Rectangle(offsetX, offsetY, inputWidth, inputHeight)
                        );
                    });

                    //FoxLog.WriteLine($"Mask size: {maskImageSharp.Width}x{maskImageSharp.Height}");

                    using var ms = new System.IO.MemoryStream();
                    maskImageSharp.SaveAsPng(ms);
                    maskImage = ms.ToArray();
                }

                // Resize and center the input image based on the calculated dimensions
                using (var originalImage = Image.Load<Rgba32>(inputImage.Image))
                using (var resizedImageSharp = new Image<Rgba32>(scaledW, scaledH))
                {
                    resizedImageSharp.Mutate(ctx =>
                    {
                        // Fill the background with black padding
                        ctx.Fill(SixLabors.ImageSharp.Color.White);

                        // Resize and draw the original image in the calculated scaled dimensions
                        //var resized = originalImage.Clone(x => x.Resize(new ResizeOptions
                        //{
                        //    Size = new Size(scaledW, scaledH),
                        //    Mode = ResizeMode.Max // Maintain aspect ratio, no distortion
                        //}));

                        ctx.DrawImage(originalImage, new Point(offsetX, offsetY), 1f); // Full opacity
                    });

                    FoxLog.WriteLine($"Image size: {resizedImageSharp.Width}x{resizedImageSharp.Height}");

                    using var ms = new System.IO.MemoryStream();
                    resizedImageSharp.SaveAsPng(ms);
                    resizedInputImage = ms.ToArray();
                }
            }

            return (maskImage, resizedInputImage);
        }

        public class WorkerEventArgs : EventArgs
        {
            // Any worker-specific event args if needed
        }

        public class TaskEventArgs : EventArgs
        {
            public FoxQueue qItem { get; }

            public TaskEventArgs(FoxQueue queueItem)
            {
                qItem = queueItem;
            }
        }
        public class TaskErrorEventArgs : EventArgs
        {
            public FoxQueue qItem { get; }
            public Exception Exception { get; }

            public TaskErrorEventArgs(FoxQueue queueItem, Exception ex)
            {
                qItem = queueItem;
                Exception = ex;
            }
        }
        public class ErrorEventArgs : EventArgs
        {
            public Exception Exception { get; }

            public ErrorEventArgs(Exception error)
            {
                Exception = error;
            }
        }

        public class ProgressUpdateEventArgs : EventArgs
        {
            public FoxQueue qItem { get; }
            public double Percent { get; }
            public double Progress { get; }

            public ProgressUpdateEventArgs(FoxQueue queueItem, double progress)
            {
                qItem = queueItem;
                Progress = progress;
                Percent = Math.Round(progress * 100, 2);
            }
        }
    }
}
