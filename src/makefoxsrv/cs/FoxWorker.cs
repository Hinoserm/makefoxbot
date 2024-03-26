using Autofocus;
using Autofocus.Models;
using makefoxsrv;
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
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Newtonsoft.Json.Linq;
using static makefoxsrv.FoxWorker;
using System.Text.RegularExpressions;
using Autofocus.Config;
using makefoxsrv.cs;

namespace makefoxsrv
{
    internal class FoxWorker
    {
        private int id;
        private CancellationTokenSource cts;
        private CancellationTokenSource cts_stop; //For gracefully stopping a generation
        private readonly string address;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(0, int.MaxValue);
        private bool semaphoreAcquired = false;
        private StableDiffusion? api;
        public bool online = true;       //Worker online status
        private FoxQueue? qitem = null;   //If we're operating, this is the current queue item being processed.
        private TelegramBotClient? botClient = null;

        public string name;

        public static ConcurrentDictionary<int, FoxWorker> workers = new ConcurrentDictionary<int, FoxWorker>();

        public double? PercentComplete = null;

        private class Lora
        {
            public string Name { get; set; }
            public string Alias { get; set; }
            public string Path { get; set; }
            public JsonElement Metadata { get; set; }
        }


        // Constructor to initialize the botClient and address
        private FoxWorker(TelegramBotClient botClient, int worker_id, string address, string name)
        {
            this.address = address;
            this.id = worker_id;
            this.name = name;
            this.cts = new CancellationTokenSource();
            this.cts_stop = new CancellationTokenSource();
            this.botClient = botClient;
        }

        private static FoxWorker CreateWorker(TelegramBotClient botClient, int worker_id, string address, string name)
        {
            var worker = new FoxWorker(botClient, worker_id, address, name);

            bool added = workers.TryAdd(worker_id, worker); // Store the worker instance
            if (!added)
                throw new Exception("Unable to add worker");

            return worker;
        }


        public static async Task<String?> GetWorkerName(int? worker_id)
        {
            if (worker_id is null)
                return null;

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);
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

        public static async Task LoadWorkers(TelegramBotClient botClient)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            MySqlCommand cmd = new MySqlCommand("SELECT id, url, name FROM workers WHERE enabled > 0", SQL);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!reader.HasRows)
                    throw new Exception("No workers available in the database.");

                while (await reader.ReadAsync())
                {
                    int id = reader.GetInt32("id");
                    string url = reader.GetString("url");
                    string name = reader.GetString("name");

                    Console.WriteLine($"Loading worker {id} - {url}");

                    var worker = CreateWorker(botClient, id, url, name);
                    await worker.SetStartDate();

                    try
                    {
                        await worker.LoadModelInfo();
                        //await worker.GetLoRAInfo();
                        await worker.SetOnlineStatus(true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Worker {worker.id} - Failed due to error: {ex.Message}");
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

                //_ = worker.Run(botClient);
                _ = Task.Run(async () => await worker.Run());

                Console.WriteLine($"Worker {worker.id} - Started.");
            }

            var qCount = await FoxQueue.GetCount();

            if (qCount > 0)
            {
                Console.WriteLine($"Begin processing of {qCount} old items in queue.");

                FoxWorker.Ping(qCount);
            }
        }

        public static bool CancelIfUserMatches(int worker_id, ulong uid)
        {
            var worker = workers[worker_id];

            if (worker.qitem is null)
                return false;

            if (worker.qitem.UID != uid)
                return false;

            //await worker.qitem.Cancel();

            worker.Stop();

            return true;
        }

        public void Stop()
        {
            Console.WriteLine($"Worker {id} stopping due to request... ");

            cts_stop.Cancel();
        }

        private async Task<StableDiffusion?> ConnectAPI(bool throw_error = true)
        {
            try
            {

                if (this.api is null)
                    this.api = new StableDiffusion(address);

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

            var models = await api.StableDiffusionModels(cts.Token);

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            //Clear the list for this worker and start fresh.
            using (var cmd = new MySqlCommand($"DELETE FROM worker_models WHERE worker_id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var model in models)
            {
                //Console.WriteLine($"  Worker {id} - Model {model_count}: {model.ModelName}");

                using (var cmd = new MySqlCommand($"INSERT INTO worker_models (worker_id, model_name, model_hash, model_sha256, model_title, model_filename, model_config) VALUES (@id, @model_name, @model_hash, @model_sha256, @model_title, @model_filename, @model_config)", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@model_name", model.ModelName);
                    cmd.Parameters.AddWithValue("@model_hash", model.Hash);
                    cmd.Parameters.AddWithValue("@model_sha256", model.SHA256);
                    cmd.Parameters.AddWithValue("@model_title", model.Title);
                    cmd.Parameters.AddWithValue("@model_filename", model.FileName);
                    cmd.Parameters.AddWithValue("@model_config", model.Config);
                    await cmd.ExecuteNonQueryAsync();
                }

                model_count++;
            }

            Console.WriteLine($"  Worker {id} - Loaded {model_count} available models");
        }

        public async Task GetLoRAInfo()
        {
            long lora_count = 0;
            long lora_tag_count = 0;

            using var httpClient = new HttpClient();
            var apiUrl = address + "/sdapi/v1/loras";

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);
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
                cmd.Parameters.AddWithValue("@workerId", id);

                await cmd.ExecuteNonQueryAsync();


                foreach (var lora in loras)
                {
                    // Attempt to insert, or update if exists
                    var insertOrUpdateCmd = new MySqlCommand(@"
                        INSERT INTO worker_loras (worker_id, name, alias, path)
                        VALUES (@workerId, @name, @alias, @path)
                        ON DUPLICATE KEY UPDATE alias=VALUES(alias), path=VALUES(path);", SQL, transaction);
                    insertOrUpdateCmd.Parameters.AddWithValue("@workerId", id);
                    insertOrUpdateCmd.Parameters.AddWithValue("@name", lora.Name);
                    insertOrUpdateCmd.Parameters.AddWithValue("@alias", lora.Alias ?? "");
                    insertOrUpdateCmd.Parameters.AddWithValue("@path", lora.Path);
                    await insertOrUpdateCmd.ExecuteNonQueryAsync();

                    // Retrieve the lora_id
                    var getLoraIdCmd = new MySqlCommand(@"
                        SELECT lora_id FROM worker_loras
                        WHERE worker_id = @workerId AND name = @name;", SQL, transaction);
                    getLoraIdCmd.Parameters.AddWithValue("@workerId", id);
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

                    var values = tagFrequencies.Select(tag => $"({loraId}, {id}, '{tag.Key.Replace("'", "''")}', {tag.Value})");
                    var insertTagsCmdText = $"INSERT INTO worker_lora_tags (lora_id, worker_id, tag_name, frequency) VALUES {string.Join(", ", values)} ON DUPLICATE KEY UPDATE frequency=VALUES(frequency);";
                    if (values.Any())
                    {
                        var insertTagsCmd = new MySqlCommand(insertTagsCmdText, SQL, transaction);
                        await insertTagsCmd.ExecuteNonQueryAsync();
                    }
                }

                // Commit the transaction
                await transaction.CommitAsync();

                Console.WriteLine($"  Worker {id} - Loaded {lora_count} LoRAs with {lora_tag_count} tags.");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error fetching LoRAs: {e.Message}");

                await transaction.RollbackAsync();
            }
        }


        public async Task LoadEmbeddingInfo()
        {
            var api = await ConnectAPI();

            if (api is null)
                throw new Exception("Unable to connect to host.");

            var embeddings = await api.Embeddings(cts.Token); // Get embeddings instead of models

            Console.WriteLine("Embeddings Information:");
            foreach (var embedding in embeddings.All)
            {
                Console.WriteLine($"Name: {embedding.Value.Name}");
                Console.WriteLine($"Step: {embedding.Value.Step}");
                Console.WriteLine($"Checkpoint: {embedding.Value.Checkpoint}");
                Console.WriteLine($"Checkpoint Name: {embedding.Value.CheckpointName}");
                Console.WriteLine($"Shape: {embedding.Value.Shape}");
                Console.WriteLine($"Vectors: {embedding.Value.Vectors}");
                Console.WriteLine("----------");
            }
        }

        public static async Task<Dictionary<string, List<int>>> GetModels()
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

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
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

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

        private static void CancelWorker(int workerId)
        {
            if (workers.TryRemove(workerId, out FoxWorker worker))
            {
                worker.cts.Cancel();
                // Perform any additional cleanup if necessary
            }
        }

        private async Task SetUsedDate(ulong? queue_id = null)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET last_queue_id = @queue_id, date_used = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("queue_id", queue_id);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetStartDate()
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET date_started = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetFailedDate(Exception ex)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET online = 0, last_error = @error, date_failed = @now WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("error", ex.Message);
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task SetOnlineStatus(bool status = true)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand($"UPDATE workers SET date_used = @now, online = @status WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("status", status);
                cmd.Parameters.AddWithValue("now", DateTime.Now);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task HandleError(Exception ex)
        {
            online = false;
            Console.WriteLine($"Worker {id} is offline!\r\n  Error: " + ex.Message);
            //await SetOnlineStatus(false); //SetFailedDate() already marks us as offline.
            await SetFailedDate(ex);

            //If we have the semaphore and crash, we better give it to someone else.
            if (semaphoreAcquired)
                semaphore.Release();

            semaphoreAcquired = false;

            qitem = null; //Clearly we're not working on an item anymore, better clear it to be safe.
        }

        private async Task MonitorProgressAsync(FoxQueue q, CancellationTokenSource cts)
        {
            int notifyTimer = 0;

            if (api is null)
                return;

            while (!cts.IsCancellationRequested && online)
            {
                try
                {
                    var p = await api.Progress(true, cts.Token);
                    this.PercentComplete = Math.Round(p.Progress * 100, 2);

                    try
                    {
                        if (this.PercentComplete > 3.0 && (notifyTimer >= 3000))
                        {
                            notifyTimer = 0;
                            await botClient.EditMessageTextAsync(
                                chatId: q.TelegramChatID,
                                messageId: q.msg_id,
                                text: $"⏳ Generating now ({(int)this.PercentComplete}%)..."
                                );
                        }
                    }
                    catch
                    {
                        //Don't care about telegram errors.
                    }

                    await Task.Delay(100, cts.Token);
                    notifyTimer += 100;
                }
                catch
                {
                    break; //API error, stop the loop.
                }
            }

            this.PercentComplete = null; //Set it back to null (IDLE) when finished.
        }

        // Example instance method for running the worker thread logic
        private async Task Run()
        {
            while (true)
            {
                try
                {
                    FoxQueue? q;
                    StableDiffusion? api;

                    //Wake up every now and then to make sure we're still online.

                    semaphoreAcquired = await semaphore.WaitAsync(2000, cts.Token);

                    //if (semaphoreAcquired)
                    //    Console.WriteLine($"Worker {id} - I have the semaphore! Semaphore count: {semaphore.CurrentCount}");

                    //Console.WriteLine($"Worker {id} - woke up, semaphore: {semaphoreAcquired}");

                    if (!online)
                    {
                        //Worker is in offline mode, just check for a working connection.
                        api = await ConnectAPI(false);

                        if (api is null)
                        {
                            //Still offline.  Wait a while and try again.

                            if (semaphoreAcquired)
                                semaphore.Release();

                            await Task.Delay(15000, cts.Token);

                            continue;
                        }
                        else
                        {
                            //Wait a little bit to let the worker stablize if it's just starting up
                            await Task.Delay(8000, cts.Token);

                            Console.WriteLine($"Worker {id} is back online!");
                            await SetOnlineStatus(true);
                            online = true;

                            await LoadModelInfo(); //Reload in case it changed.
                        }
                    }

                    api = await ConnectAPI();

                    if (!online) {
                        await SetOnlineStatus(true); //Appears to be working now.
                        online = true;

                        Console.WriteLine($"Worker {id} came back online unexpectedly");
                    }

                    var status = await api.QueueStatus();
                    var progress = await api.Progress(true);

                    //We only need to do this check if the semaphore was aquired (only if work is waiting for us)
                    if (semaphoreAcquired && (status.QueueSize > 0 || progress.State.JobCount > 0))
                    {

                        int waitMs = (status.QueueSize + progress.State.JobCount) * 500;
                        //Console.WriteLine($"Worker {id} - URL has a busy queue, waiting {waitMs}ms...");

                        if (semaphoreAcquired)
                            semaphore.Release();

                        await Task.Delay(waitMs, cts.Token);

                        continue;
                    }

                    //while ((q = await FoxQueue.Pop(id)) is not null) //Work until the queue is empty
                    //{
                    (q, int processingCount) = await FoxQueue.Pop(id);

                    if (q is null)
                    {
                        //If we have the semaphore, but couldn't find anything to process.
                        //Better give it to someone else.


                        if (semaphoreAcquired)
                        {
                            if (processingCount > 0)
                            {
                                //Console.WriteLine($"Worker {id} - Incompatible queue item, returning semaphore!");

                                semaphore.Release();
                            }
                            else
                                Console.WriteLine($"Worker {id} - That's odd... we have the semaphore, but can't find a queue item!");
                        }

                        continue;
                    }

                    //Console.WriteLine($"{address} - {q.settings.model}");

                    //Console.WriteLine($"Starting image {q.id}...");

                    qitem = q;

                    try
                    {

                        api = await ConnectAPI(true); // It's been a while, we should ping the API again.

                        await q.SetWorker(id);

                        await SetUsedDate(q.id);

                        using var comboCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cts_stop.Token);

                        try
                        {
                            await botClient.EditMessageTextAsync(
                                chatId: q.TelegramChatID,
                                messageId: q.msg_id,
                                text: $"⏳ Generating now..."
                            );
                        }
                        catch (Exception ex) {
                            //If we can't edit, we probably hit a rate limit with this user.
                            

                            Match match = Regex.Match(ex.Message, @"Too Many Requests: retry after (\d+)");

                            if (match.Success)
                            {
                                // If the message matches, extract the number
                                int retryAfterSeconds = int.Parse(match.Groups[1].Value) + 3;
                                Console.WriteLine($"Worker {id} - Rate limit exceeded. Try again after {retryAfterSeconds} seconds.");

                                // Wait for the specified number of seconds before retrying
                                //await Task.Delay(retryAfterSeconds * 1000);

                                //Ideally we need some way to set a "retryAfterSecs" item on this request in the queue.

                                await HandleError(ex);
                                continue;
                            } else
                                Console.WriteLine("Edit msg failed " + ex.Message); //We don't care if editing fails in other cases, like the message being missing.
                        } 

                        var settings = q.settings;                            

                        using CancellationTokenSource progress_cts = new CancellationTokenSource();

                        using var comboBreaker = CancellationTokenSource.CreateLinkedTokenSource(comboCts.Token, progress_cts.Token);

                        _= this.MonitorProgressAsync(q, comboBreaker);

                        if (q.type == "IMG2IMG")
                        {
                            q.input_image = await FoxImage.Load(settings.selected_image);

                            if (q.input_image is null)
                            {
                                await q.Finish();
                                throw new Exception("The selected image was unable to be located");
                            }

                            //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                            //var model = await api.StableDiffusionModel("indigoFurryMix_v90Hybrid"); //
                            var model = await api.StableDiffusionModel(settings.model, comboCts.Token);
                            var sampler = await api.Sampler("DPM++ 2M Karras", comboCts.Token);
                            //var sampler = await api.Sampler("Restart", comboCts.Token);

                            var img = new Base64EncodedImage(q.input_image.Image);

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

                                    DenoisingStrength = (double)settings.denoising_strength,

                                    Sampler = new()
                                    {
                                        Sampler = sampler,
                                        SamplingSteps = settings.steps,
                                        CfgScale = (double)settings.cfgscale
                                    },
                                }
                            , comboCts.Token);

                            await q.SaveOutputImage(img2img.Images.Last().Data.ToArray());
                        }
                        else if (q.type == "TXT2IMG")
                        {
                            //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                            //var model = await api.StableDiffusionModel("indigoFurryMix_v90Hybrid");
                            var model = await api.StableDiffusionModel(settings.model, comboCts.Token);
                            var sampler = await api.Sampler("DPM++ 2M Karras", comboCts.Token);
                            //var sampler = await api.Sampler("Restart", comboCts.Token);

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

                                    Width = settings.width,
                                    Height = settings.height,

                                    Sampler = new()
                                    {
                                        Sampler = sampler,
                                        SamplingSteps = settings.steps,
                                        CfgScale = (double)settings.cfgscale
                                    },
                                }
                            , comboCts.Token);

                            await q.SaveOutputImage(txt2img.Images.Last().Data.ToArray());
                        }

                        progress_cts.Cancel();

                        await q.Finish();
                        _ = FoxSendQueue.Enqueue(botClient, q);

                        //Console.WriteLine($"Finished image {q.id}.");

                    }
                    catch (OperationCanceledException)
                    {
                        // Handle the cancellation specifically
                        Console.WriteLine($"Worker {id} - User Cancellation");

                        await q.Cancel();

                        cts_stop = new CancellationTokenSource();

                        try {
                            using var httpClient = new HttpClient();

                            var response = await httpClient.PostAsync(address + "/sdapi/v1/interrupt", null);
                            response.EnsureSuccessStatusCode();

                            _ = botClient.EditMessageTextAsync(
                                chatId: qitem.TelegramChatID,
                                messageId: qitem.msg_id,
                                text: "❌ Cancelled."
                            );
                        } catch { throw;  }

                        qitem = null;
                    }
                    catch (Exception ex)
                    { 
                        try
                        {
                            await q.Finish(ex); //Mark it as ERROR'd.
                        }
                        catch { }

                        try
                        {
                            await botClient.EditMessageTextAsync(
                                chatId: q.TelegramChatID,
                                messageId: q.msg_id,
                                text: $"⏳ Error (will re-attempt soon)"
                            );
                        }
                        catch { }

                        await HandleError(ex);


                        //break; //Leave the queue loop.
                    }

                    qitem = null;
                 
                    //Console.WriteLine("Worker Tick...");
                }
                catch (Exception ex)
                {
                    await HandleError(ex);
                    //Console.WriteLine("Error worker: " + ex.Message);
                    //await Task.Delay(3000, cts.Token);
                }
            }
        }
    }
}
