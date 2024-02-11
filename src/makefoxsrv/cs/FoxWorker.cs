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
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

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

                    Console.WriteLine($"Starting worker {id} - {url}\r\n");

                    var worker = CreateWorker(botClient, id, url);

                    await worker.LoadModelInfo();
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
                Console.WriteLine($"Worker {id} - Model: {model.ModelName}");

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
