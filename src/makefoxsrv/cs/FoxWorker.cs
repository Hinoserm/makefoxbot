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

namespace makefoxsrv
{
    internal class FoxWorker
    {
        private int id;
        private CancellationTokenSource cts;
        private readonly string address;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(0);
        private StableDiffusion? api;

        private static ConcurrentDictionary<int, FoxWorker> workers = new ConcurrentDictionary<int, FoxWorker>();


        // Constructor to initialize the botClient and address
        private FoxWorker(TelegramBotClient botClient, int worker_id, string address)
        {
            this.address = address;
            this.id = worker_id;
            this.cts = new CancellationTokenSource();
        }

        private static FoxWorker CreateWorker(TelegramBotClient botClient, int worker_id, string address)
        {
            var worker = new FoxWorker(botClient, worker_id, address);

            bool added = workers.TryAdd(worker_id, worker); // Store the worker instance
            if (!added)
                throw new Exception("Unable to add worker");

            return worker;
        }

        public static async Task StartWorkers(TelegramBotClient botClient)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            MySqlCommand cmd = new MySqlCommand("SELECT id, url FROM workers WHERE enabled > 0", SQL);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!reader.HasRows)
                    throw new Exception("No workers available in the database.");

                while (await reader.ReadAsync())
                {
                    int id = reader.GetInt32("id");
                    string url = reader.GetString("url");

                    Console.WriteLine($"Starting worker {id} - {url}");

                    var worker = CreateWorker(botClient, id, url);

                    await worker.LoadModelInfo();
                    await worker.GetLoRAInfo();
                    await worker.SetStartDate();

                    _ = worker.Run(botClient);
                }
            }
        }
        private async Task<StableDiffusion?> ConnectAPI()
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

        public class Lora
        {
            public string Name { get; set; }
            public string Alias { get; set; }
            public string Path { get; set; }
            public JsonElement Metadata { get; set; }
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

            /*

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            // Assuming you have a similar table structure for embeddings, adjust the table name and fields as necessary.
            // Clear the list for this worker and start fresh.
            using (var cmd = new MySqlCommand($"DELETE FROM worker_loras WHERE worker_id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            // Iterate over the embeddings and insert their details into the database.
            foreach (var embedding in embeddings.All)
            {
                Console.WriteLine($"Worker {id} - Embedding: {embedding.Value.Name}");

                using (var cmd = new MySqlCommand($"INSERT INTO worker_loras (worker_id, embedding_name, step, checkpoint, checkpoint_name, shape, vectors) VALUES (@id, @embedding_name, @step, @checkpoint, @checkpoint_name, @shape, @vectors)", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@embedding_name", embedding.Value.Name);
                    cmd.Parameters.AddWithValue("@step", embedding.Value.Step ?? (object)DBNull.Value); // Handling nullable int
                    cmd.Parameters.AddWithValue("@checkpoint", embedding.Value.Checkpoint);
                    cmd.Parameters.AddWithValue("@checkpoint_name", embedding.Value.CheckpointName);
                    cmd.Parameters.AddWithValue("@shape", embedding.Value.Shape);
                    cmd.Parameters.AddWithValue("@vectors", embedding.Value.Vectors);
                    await cmd.ExecuteNonQueryAsync();
                }
            } */
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
                        AND w.online = TRUE";

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

        public static void Ping()
        {
            semaphore.Release();
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

        // Example instance method for running the worker thread logic
        private async Task Run(TelegramBotClient botClient)
        {
            while (true)
            {
                try
                {

                    FoxQueue? q;

                    var api = await ConnectAPI();

                    if (api is null)
                        throw new Exception("Connection lost");

                    await SetOnlineStatus(true); //Appears to be working.

                    var status = await api.QueueStatus();
                    var progress = await api.Progress(true);

                    if (status.QueueSize > 0 || progress.State.JobCount > 0)
                    {
                        //Console.WriteLine($"Queue busy, waiting 800ms...");
                        await Task.Delay(800);
                        continue;
                    }

                    while ((q = await FoxQueue.Pop(id)) is not null) //Work until the queue is empty
                    {
                        //Console.WriteLine($"Starting image {q.id}...");

                        try
                        {
                            await q.SetWorker(address);

                            await api.Ping();

                            await SetUsedDate(q.id);

                            try
                            {
                                await botClient.EditMessageTextAsync(
                                    chatId: q.TelegramChatID,
                                    messageId: q.msg_id,
                                    text: $"⏳ Generating now..."
                                );
                            }
                            catch { } //We don't care if editing fails.

                            var settings = q.settings;

                            /*

                            CancellationTokenSource progress_cts = new CancellationTokenSource();

                            _ = Task.Run(async () =>
                            {
                                while (!progress_cts.IsCancellationRequested)
                                {
                                    var progress = api.Progress().Result.Progress * 100;

                                    await botClient.EditMessageTextAsync(
                                        chatId: q.TelegramChatID,
                                        messageId: q.msg_id,
                                        text: $"⏳ Generating now ({progress})..."
                                        );
                                    await Task.Delay(500);

                                }
                            });
                            */

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
                                var model = await api.StableDiffusionModel(settings.model);
                                var sampler = await api.Sampler("DPM++ 2M Karras");

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
                                            Seed = settings.seed,
                                        },

                                        DenoisingStrength = (double)settings.denoising_strength,

                                        Sampler = new()
                                        {
                                            Sampler = sampler,
                                            SamplingSteps = settings.steps,
                                            CfgScale = (double)settings.cfgscale
                                        },
                                    }
                                );

                                await q.SaveOutputImage(img2img.Images.Last().Data.ToArray());
                            }
                            else if (q.type == "TXT2IMG")
                            {
                                //var cnet = await api.TryGetControlNet() ?? throw new NotImplementedException("no controlnet!");

                                //var model = await api.StableDiffusionModel("indigoFurryMix_v90Hybrid");
                                var model = await api.StableDiffusionModel(settings.model);
                                var sampler = await api.Sampler("DPM++ 2M Karras");

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
                                );

                                await q.SaveOutputImage(txt2img.Images.Last().Data.ToArray());
                            }

                            await q.Finish();
                            _ = FoxSendQueue.Enqueue(botClient, q);
                            _ = FoxQueue.NotifyUserPositions(botClient);


                            //Console.WriteLine($"Finished image {q.id}.");

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Worker error: " + ex.Message);

                            try
                            {
                                await q.Finish(ex);
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
                        }
                    }

                    await semaphore.WaitAsync(2000, cts.Token);
                    //Console.WriteLine("Worker Tick...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error worker: " + ex.Message);
                    await Task.Delay(3000);
                }
            }
        }
    }
}
