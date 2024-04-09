using Autofocus;
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
using Terminal.Gui;
using static NStack.Unicode;
using Autofocus.Scripts;
using System.Configuration;

namespace makefoxsrv
{
    internal class FoxWorker
    {
        public int ID { get; private set; }

        private CancellationTokenSource stopToken = new(); //Token to stop this worker specifically

        private readonly string address;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(0, int.MaxValue);
        private bool semaphoreAcquired = false;
        private StableDiffusion? api;
        public FoxQueue? qItem = null;   //If we're operating, this is the current queue item being processed.

        public int? MaxImageSize { get; private set; } //width*height.  If null, no limit
        public int? MaxImageSteps { get; private set; } //If null, no limit
        public int? MaxUpscaleSize { get; private set; } //If null, does not support upscaling

        public DateTime StartDate { get; private set; } //Worker start date
        public DateTime? TaskStartDate { get; private set; } = null;
        
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
        private static CancellationToken cancellationToken;

        public IProgress? Progress = null;

        public Dictionary<string, int> availableModels { get; private set; } = new Dictionary<string, int>();

        private LinkedList<string>? recentModels = new LinkedList<string>();
        private Dictionary<string, LinkedListNode<string>>? modelNodes = new Dictionary<string, LinkedListNode<string>>();
        private int _modelCapacity;

        public int ModelCapacity
        {
            get => _modelCapacity;
            set
            {
                _modelCapacity = value;

                if (_modelCapacity == 0)
                {
                    // Clear tracking structures if capacity is set to 0.
                    if (recentModels is not null)
                        recentModels?.Clear();
                    if (modelNodes is not null)
                        modelNodes?.Clear();

                    recentModels = null;
                    modelNodes = null;
                }
                else
                {
                    // Ensure structures are initialized (they are by default, but this is for clarity and future-proofing).
                    if (recentModels is null)
                        recentModels = new LinkedList<string>();
                    if (modelNodes is null)
                        modelNodes = new Dictionary<string, LinkedListNode<string>>();

                    // If increasing capacity from 0, no need to trim structures, as they were just cleared or are already initialized.
                    // Only trim if the count exceeds the new, non-zero capacity.
                    while (recentModels.Count > _modelCapacity)
                    {
                        var oldestModel = recentModels.First?.Value;
                        if (oldestModel is not null)
                        {
                            recentModels.RemoveFirst();
                            modelNodes.Remove(oldestModel);
                        }
                    }
                }
            }
        }

        public void UseModel(string model)
        {
            if (string.IsNullOrEmpty(model) || _modelCapacity == 0)
                return; // Do not track if capacity is 0 or model is invalid.

            if (modelNodes is null || recentModels is null)
                return; // Do not track if structures are not initialized.

            //FoxLog.WriteLine($"Worker {ID} - Using model: {model}");

            if (modelNodes.TryGetValue(model, out var existingNode))
            {
                // Update the model's position to the end as the most recently used.
                recentModels.Remove(existingNode);
                recentModels.AddLast(existingNode);
            }
            else
            {
                // Add a new model, ensuring capacity constraints are respected.
                if (recentModels.Count >= ModelCapacity)
                {
                    var oldestModel = recentModels.First?.Value;
                    if (oldestModel is not null) {
                        recentModels.RemoveFirst();
                        modelNodes.Remove(oldestModel);
                    }
                }

                var newNode = new LinkedListNode<string>(model);
                recentModels.AddLast(newNode);
                modelNodes[model] = newNode;
            }
        }

        public IEnumerable<string> GetRecentModels() => (recentModels is not null) ? recentModels.AsEnumerable() : Enumerable.Empty<string>();

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

            FoxWorker.cancellationToken = cancellationToken;

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

                    if (!(reader["max_upscale_size"] is DBNull))
                        worker.MaxUpscaleSize = reader.GetInt32("max_upscale_size");

                    await worker.SetStartDate();

                    try
                    {
                        await worker.LoadModelInfo();
                        //await worker.GetLoRAInfo();
                        await worker.SetOnlineStatus(true);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine($"Worker {worker.ID} - Failed due to error: {ex.Message}");
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

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.stopToken.Token);

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
            long model_count = 0;
            var api = await ConnectAPI();

            if (api is null)
                throw new Exception("Unable to connect to host.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.stopToken.Token);

            var models = await api.StableDiffusionModels(cts.Token);

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            //Clear the list for this worker and start fresh.
            using (var cmd = new MySqlCommand($"DELETE FROM worker_models WHERE worker_id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", ID);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var model in models)
            {
                //FoxLog.WriteLine($"  Worker {id} - Model {model_count}: {model.ModelName}");

                using (var cmd = new MySqlCommand($"INSERT INTO worker_models (worker_id, model_name, model_hash, model_sha256, model_title, model_filename, model_config) VALUES (@id, @model_name, @model_hash, @model_sha256, @model_title, @model_filename, @model_config)", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", ID);
                    cmd.Parameters.AddWithValue("@model_name", model.ModelName);
                    cmd.Parameters.AddWithValue("@model_hash", model.Hash);
                    cmd.Parameters.AddWithValue("@model_sha256", model.SHA256);
                    cmd.Parameters.AddWithValue("@model_title", model.Title);
                    cmd.Parameters.AddWithValue("@model_filename", model.FileName);
                    cmd.Parameters.AddWithValue("@model_config", model.Config);
                    await cmd.ExecuteNonQueryAsync();
                }

                model_count++;

                if (!availableModels.ContainsKey(model.ModelName))
                {
                    availableModels.Add(model.ModelName, ID);
                }
            }

            FoxLog.WriteLine($"  Worker {ID} - Loaded {model_count} available models");
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
            catch (HttpRequestException e)
            {
                FoxLog.WriteLine($"Error fetching LoRAs: {e.Message}");

                await transaction.RollbackAsync();
            }
        }


        public async Task LoadEmbeddingInfo()
        {
            var api = await ConnectAPI();

            if (api is null)
                throw new Exception("Unable to connect to host.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.stopToken.Token);

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

        public static async Task<Dictionary<string, List<int>>> GetAvailableModels()
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            var workerIds = FoxWorker.GetActiveWorkerIds(); // Assume this returns List<int> of active worker IDs

            if (!workerIds.Any())
                throw new Exception("No active workers.");

            // Dynamically building the IN clause directly in the command text like this poses a risk of SQL injection.
            // Be absolutely sure that workerIds are safe (i.e., strictly controlled/validated as integers).
            var workerIdParams = string.Join(", ", workerIds);
            var cmdText = $@"
                    SELECT
                        wm.model_name, wm.worker_id
                    FROM
                        worker_models wm
                    INNER JOIN
                        workers w ON wm.worker_id = w.id
                    WHERE
                        wm.worker_id IN ({workerIdParams})
                        AND w.online = TRUE
                    ORDER BY
                        wm.model_name ASC;";

            MySqlCommand cmd = new MySqlCommand(cmdText, SQL);

            var models = new Dictionary<string, List<int>>();

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string modelName = reader.GetString("model_name");
                    int workerId = reader.GetInt32("worker_id");

                    if (!models.ContainsKey(modelName))
                    {
                        models[modelName] = new List<int>();
                    }

                    models[modelName].Add(workerId);
                }
            }

            return models;
        }

        public static async Task<List<int>?> GetWorkersForModel(string modelName)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            var workerIds = FoxWorker.GetActiveWorkerIds(); // Assume this returns List<int> of active worker IDs

            if (!workerIds.Any())
                throw new Exception("No active workers.");

            // Dynamically building the IN clause directly in the command text like this poses a risk of SQL injection.
            // Be absolutely sure that workerIds are safe (i.e., strictly controlled/validated as integers).
            var workerIdParams = string.Join(", ", workerIds);
            var cmdText = $@"
                    SELECT
                        wm.worker_id
                    FROM
                        worker_models wm
                    INNER JOIN
                        workers w ON wm.worker_id = w.id
                    WHERE
                        wm.worker_id IN ({workerIdParams})
                        AND w.online = TRUE
                        AND wm.model_name = @modelName"; // Filter by the provided model name

            MySqlCommand cmd = new MySqlCommand(cmdText, SQL);
            cmd.Parameters.AddWithValue("@modelName", modelName); // Use parameterization to avoid SQL injection

            var workersForModel = new List<int>();

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!reader.HasRows)
                    return null; // Return null if no workers are found for the model

                while (await reader.ReadAsync())
                {
                    int workerId = reader.GetInt32("worker_id");
                    workersForModel.Add(workerId);
                }
            }

            return workersForModel;
        }


        public static ICollection<int> GetActiveWorkerIds()
        {
            return workers.Keys;
        }

        public static void Ping(int count = 1)
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
                    FoxLog.WriteLine($"Error running OnTaskError: {ex2.Message}\r\n{ex2.StackTrace}");
                }
            }

            try
            {
                OnWorkerError?.Invoke(this, new ErrorEventArgs(ex));
            }
            catch (Exception ex2)
            {
                FoxLog.WriteLine($"Error running OnWorkerError: {ex2.Message}\r\n{ex2.StackTrace}");
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

        private CancellationTokenSource StartProgressMonitor(FoxQueue qItem, CancellationToken cancellationToken)
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

                        if (p.Progress > lastProgress)
                        {
                            if (!Online)
                                break;

                            if (qItem is null)
                                break;

                            lastProgress = p.Progress;

                            float progressPercent = (float)Math.Round(p.Progress * 100, 2);

                            this.Progress = p;

                            if (qItem is not null)
                            {
                                if (qItem.IsFinished()) // Cancelled or finished prematurely, shut down.
                                    break;

                                OnTaskProgress?.Invoke(this, new ProgressUpdateEventArgs(qItem, p));
                                _= qItem.Progress(this, p, progressPercent);
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
                        FoxLog.WriteLine($"Worker {ID} - Progress monitor error: {ex.Message}");
                        break; //API error, stop the loop.
                    }
                }

                this.Progress = null; //Set it back to null when finished.
            });

            return progressCTS;
        }

        private async Task Start()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken.Token);

            //var waitHandles = new WaitHandle[] { enabledEvent, cts.Token.WaitHandle };

            this.ModelCapacity = 3;

            OnWorkerStart?.Invoke(this, new WorkerEventArgs());            

            _ = Task.Run(async () =>
            {
                Online = await ConnectAPI(false) is not null;

                while (!cts.IsCancellationRequested)
                {
                    var waitMs = 6000; //Default wait time

                    try
                    {
                        var semaphoreAquired = await semaphore.WaitAsync(waitMs, cts.Token);

                        //WaitHandle.WaitAny(waitHandles);

                        api = await ConnectAPI(false);

                        var newOnlineStatus = api is not null;

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
                                    Online = false;
                                    var busyWaitTime = (status.QueueSize + progress.State.JobCount) * 500;

                                    //FoxLog.WriteLine($"Worker {ID} - Busy. Waiting {busyWaitTime}ms.");

                                    await Task.Delay(busyWaitTime, cts.Token);

                                    continue;
                                }
                                else
                                {
                                    Online = true;
                                    break;
                                }
                            }
                        }

                        if (Online && api is not null && qItem is not null)
                        {
                            await ProcessTask(cts.Token);
                            if (qItem is not null)
                            {
                                FoxLog.WriteLine($"Processing completed, but qItem is not null!");
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
                catch {
                    //Error?
                }

                FoxLog.WriteLine($"Worker {ID} - Shutdown.");
                workers.TryRemove(ID, out _);
            });
        }

        public async Task ProcessTask(CancellationToken ctToken)
        {
            CancellationTokenSource? progressCTS = null;

            try
            {
                if (qItem is null)
                    throw new Exception("Attempt to process task when no task was assigned");

                FoxLog.WriteLine($"Worker {this.name} is now processing task {qItem.ID}", LogLevel.DEBUG);

                if (api is null)
                    throw new Exception("API not available (Should have been loaded before we got here)");

                this.TaskStartDate = DateTime.Now;

                await qItem.SetWorker(this);

                await this.SetUsedDate();

                var ctsLoop = CancellationTokenSource.CreateLinkedTokenSource(ctToken, qItem.stopToken.Token);

                OnTaskStart?.Invoke(this, new TaskEventArgs(qItem));
                await qItem.Start(this);

                var settings = qItem.Settings;

                this.UseModel(settings.model);

                progressCTS = StartProgressMonitor(qItem, ctsLoop.Token);

                Byte[] outputImage;

                FoxQueue.QueueType generationType = qItem.Type;

                if (generationType == FoxQueue.QueueType.IMG2IMG)
                {
                    //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                    var model = await api.StableDiffusionModel(settings.model, ctsLoop.Token);
                    var sampler = await api.Sampler("DPM++ 2M Karras", ctsLoop.Token);


                    FoxImage? inputImage = await qItem.GetInputImage();

                    if (inputImage is null || inputImage.Image is null)
                        throw new Exception("Input image could not be loaded.");

                    var img = new Base64EncodedImage(inputImage.Image);

                    var img2img = await api.Image2Image(
                        new()
                        {
                            Images = { img },

                            Model = model,

                            Prompt = new()
                            {
                                Positive = settings.prompt,
                                Negative = settings.negative_prompt,
                            },

                            Width = settings.width,
                            Height = settings.height,

                            Seed = new()
                            {
                                Seed = settings.seed
                            },
                            ResizeMode = qItem.Enhanced ? 0 : 2, //Testing this
                            InpaintingFill = MaskFillMode.LatentNoise,
                            DenoisingStrength = (double)settings.denoising_strength,

                            Sampler = new()
                            {
                                Sampler = sampler,
                                SamplingSteps = settings.steps,
                                CfgScale = (double)settings.cfgscale
                            }
                        }
                    , ctsLoop.Token);

                    outputImage = img2img.Images.Last().Data.ToArray();
                }
                else if (generationType == FoxQueue.QueueType.TXT2IMG)
                {
                    //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                    //var model = await api.StableDiffusionModel("indigoFurryMix_v90Hybrid");
                    var model = await api.StableDiffusionModel(settings.model, ctsLoop.Token);
                    var sampler = await api.Sampler("DPM++ 2M Karras", ctsLoop.Token);
                    //var sampler = await api.Sampler("Restart", ctsLoop.Token);

                    var width = settings.width;
                    var height = settings.height;

                    HighResConfig? hiResConfig = null;

                    if (settings.width > 1024 || settings.height > 1024)
                    {
                        var upscaler = await api.Upscaler("R-ESRGAN 4x+", ctsLoop.Token);
                        hiResConfig = new HighResConfig()
                        {
                            Width = settings.width,
                            Height = settings.height,
                            DenoisingStrength = 0.55,
                            Upscaler = upscaler,
                            Steps = 15
                        };

                        (width, height) = FoxImage.CalculateLimitedDimensions(settings.width, settings.height, 768);
                    }

                    var txt2img = await api.TextToImage(
                        new()
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
                        }
                    , ctsLoop.Token);

                    outputImage = txt2img.Images.Last().Data.ToArray();
                }
                else
                    throw new NotImplementedException("Request type not implemented.");

                if (progressCTS is not null)
                    progressCTS.Cancel();

                try {
                    await qItem.Send(outputImage);
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Error on worker {this.name} while sending task {qItem.ID}: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
                }

                try
                {
                    OnTaskCompleted?.Invoke(this, new TaskEventArgs(qItem));
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Error on worker {this.name} while running OnTaskCompleted for task {qItem.ID}: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
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
                        FoxLog.WriteLine($"Worker {ID} - Rate limit exceeded. Trying again after {retryAfterSeconds} seconds.");

                        if (qItem is not null)
                        {
                            try
                            {
                                await qItem.SetError(ex, DateTime.Now.AddSeconds(retryAfterSeconds + 65));
                                OnTaskError?.Invoke(this, new TaskErrorEventArgs(qItem, ex));
                            } catch (Exception ex2)
                            {
                                FoxLog.WriteLine($"Error running OnTaskError: {ex2.Message}\r\n{ex2.StackTrace}");
                            }
                            _ = FoxQueue.Enqueue(qItem);
                        }
                    }
                    else if (ex.Message == "INPUT_USER_DEACTIVATED")
                    {
                        FoxLog.WriteLine($"Worker {ID} - User deactivated. Stopping task.");

                        if (qItem is not null)
                        {
                            try
                            {
                                await qItem.SetCancelled();
                                OnTaskCancelled?.Invoke(this, new TaskEventArgs(qItem));
                            } catch (Exception ex2)
                            {
                                FoxLog.WriteLine($"Error running OnTaskCancelled: {ex2.Message}\r\n{ex2.StackTrace}");
                            }
                        }
                    }
                    else
                    {
                        FoxLog.WriteLine($"Telegram Error: {ex.Message}");
                        await HandleError(ex);
                    }
                }
                else //We don't care about other telegram errors, but log them for debugging purposes.
                    FoxLog.WriteLine($"Telegram error {ex.Message}\r\n{ex.StackTrace}");
            }
            catch (OperationCanceledException ex)
            {
                // Handle the cancellation specifically
                if (qItem is not null && qItem.stopToken.IsCancellationRequested)
                {
                    FoxLog.WriteLine($"Worker {ID} - User Cancelled");

                    try
                    {
                        OnTaskCancelled?.Invoke(this, new TaskEventArgs(qItem));

                        await qItem.SetCancelled();

                        using var httpClient = new HttpClient();

                        var response = await httpClient.PostAsync(address + "/sdapi/v1/interrupt", null, cancellationToken);
                        response.EnsureSuccessStatusCode();
                    } catch (Exception ex2)
                    {
                        FoxLog.WriteLine($"Error running OnTaskCancelled: {ex2.Message}\r\n{ex2.StackTrace}");
                    }
                }
                else
                    await HandleError(ex);
            }
            finally
            {
                if (progressCTS is not null)
                    progressCTS.Cancel();

                qItem = null;
                TaskStartDate = null;
            }
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
            public IProgress Progress { get; }

            public ProgressUpdateEventArgs(FoxQueue queueItem, IProgress progress)
            {
                qItem = queueItem;
                Progress = progress;
                Percent = Math.Round(progress.Progress * 100, 2);
            }
        }
    }
}
