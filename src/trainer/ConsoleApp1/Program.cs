


using MySqlConnector;
using System.Runtime.InteropServices;
using Config.Net;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.AutoML;
using Microsoft.ML.Trainers.LightGbm;
using System.Reflection;
using System.Collections.Immutable;
using Microsoft.ML; // Base ML.NET functionality
using Microsoft.ML.Data;
using Microsoft.ML.Vision; // For image classification and computer vision tasks
using Microsoft.ML.TensorFlow; // For using TensorFlow models
using Microsoft.ML.OnnxRuntime; // For ONNX models
using Microsoft.ML.Transforms.TimeSeries; // For time series analysis

public interface IMySettings
{
    [Option(Alias = "Telegram.BOT_TOKEN")]
    string TelegramBotToken { get; }

    [Option(Alias = "Telegram.PAYMENT_TOKEN")]
    string TelegramPaymentToken { get; }

    [Option(Alias = "Telegram.API_URL")]
    string TelegramApiUrl { get; }

    [Option(Alias = "Telegram.API_ID")]
    int? TelegramApiId { get; }

    [Option(Alias = "Telegram.API_HASH")]
    string TelegramApiHash { get; }

    [Option(Alias = "MySQL.USERNAME")]
    string MySQLUsername { get; }
    [Option(Alias = "MySQL.PASSWORD")]
    string MySQLPassword { get; }
    [Option(Alias = "MySQL.SERVER")]
    string MySQLServer { get; }
    [Option(Alias = "MySQL.DATABASE")]
    string MySQLDatabase { get; }
}

namespace FoxTrainer
{
    public enum RequestType
    {
        UNKNOWN,
        IMG2IMG,
        TXT2IMG
    }

    public class RequestData
    {
        public string Model { get; set; }
        public string Prompt { get; set; }
        public string NegativePrompt { get; set; }

        //public float PromptSize { get; set; }
        //public float NegativePromptSize { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Steps { get; set; }
        public float WorkerId { get; set; }
        public float ProcessingTime { get; set; } // In seconds, with some precision for milliseconds

        //public float CFGScale { get; set; }
        //
        //public float Type { get; set; }

        //public DateTime StartDate;

        public bool IsWorkerIdKnown { get; set; } = true; // New property, default true for training data

    }

    internal class Program
    {

        public static List<RequestData> Data = new List<RequestData>();

        public static IMySettings? settings = new ConfigurationBuilder<IMySettings>()
.UseIniFile("../bin/settings.ini")
.Build();

        public static string sqlConnectionString =
            $"Server={settings.MySQLServer};" +
            $"User ID={settings.MySQLUsername};" +
            $"Password={settings.MySQLPassword};" +
            $"Database={settings.MySQLDatabase};" +
            $"charset=utf8mb4;" +
            $"keepalive=60;" +
            $"pooling=true;" +
            $"minpoolsize=3;" +
            $"maxpoolsize=15;" +
            $"default command timeout=180;";

        static void LoadSettings(string filename = "settings.ini")
        {
            if (settings is null)
                throw new Exception($"Settings file {filename} could not be loaded.");

            if (string.IsNullOrEmpty(settings.TelegramBotToken))
                throw new Exception("Missing setting Telegram.bot_token is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLUsername))
                throw new Exception("Missing setting MySQL.username is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLPassword))
                throw new Exception("Missing setting MySQL.password is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLServer))
                throw new Exception("Missing setting MySQL.server is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLDatabase))
                throw new Exception("Missing setting MySQL.database is not optional.");

        }

        private static Random rng = new Random(123); // Seed for determinism

        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            var cts = new CancellationTokenSource();

            Console.Write("Loading configuration... ");
            try
            {
                LoadSettings();

                if (settings is null)
                    throw new Exception("Unable to load settings.ini");

                if (settings.TelegramApiId is null)
                    throw new Exception("API_ID setting not set.");
                if (settings.TelegramApiHash is null)
                    throw new Exception("API_HASH setting not set.");
                if (settings.TelegramBotToken is null)
                    throw new Exception("BOT_TOKEN setting not set.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("error: " + ex.Message);

                return;
            }
            Console.WriteLine("done.");

            Console.Write("Connecting to database... ");
            try
            {
                MySqlConnection sql = new MySqlConnection(Program.sqlConnectionString);
                await sql.OpenAsync(cts.Token);
                await sql.PingAsync(cts.Token);
                await sql.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("error: " + ex.Message);
                return;
            }
            Console.WriteLine("done.");

            Console.WriteLine("Loading data from database...");
            using var SQL = new MySqlConnection(Program.sqlConnectionString);

            await SQL.OpenAsync();

            var count = 0;

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = @"
                        SELECT
                            * FROM queue
                        WHERE 
                            status = 'FINISHED' AND
                            date_worker_start IS NOT NULL AND
                            date_sent IS NOT NULL AND
                            date_added IS NOT NULL AND
                            worker IS NOT NULL AND
                            (enhanced != 1 OR enhanced IS NULL)
                        ORDER BY RAND()
                        LIMIT 1000000; 
                        ";

                using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    var q = new RequestData();

                    //q.Type = (int)Enum.Parse<RequestType>(Convert.ToString(r["type"]) ?? "UNKNOWN");

                    if (!(r["steps"] is DBNull))
                        q.Steps = Convert.ToInt16(r["steps"]);
                    //if (!(r["cfgscale"] is DBNull))
                    //    q.CFGScale = (float)Convert.ToDouble(r["cfgscale"]);
                    if (!(r["prompt"] is DBNull))
                    {
                        q.Prompt = Convert.ToString(r["prompt"]);
                        //q.PromptSize = Convert.ToString(r["prompt"]).Length;
                    }
                    if (!(r["negative_prompt"] is DBNull))
                    {
                        q.NegativePrompt = Convert.ToString(r["negative_prompt"]);
                        //q.NegativePromptSize = Convert.ToString(r["negative_prompt"]).Length;
                    }
                    if (!(r["width"] is DBNull))
                        q.Width = Convert.ToInt32(r["width"]);
                    if (!(r["height"] is DBNull))
                        q.Height = Convert.ToInt32(r["height"]);
                    if (!(r["worker"] is DBNull))
                        q.WorkerId = Convert.ToInt32(r["worker"]);
                    //if (!(r["denoising_strength"] is DBNull))
                    //    settings.denoising_strength = Convert.ToDecimal(r["denoising_strength"]);
                    //if (!(r["seed"] is DBNull))
                    //    settings.seed = Convert.ToInt32(r["seed"]);
                    if (!(r["model"] is DBNull))
                        q.Model = Convert.ToString(r["model"]);
                    //if (!(r["date_added"] is DBNull))
                        //q.StartDate = Convert.ToDateTime(r["date_added"]);

                    if (!(r["date_sent"] is DBNull) && !(r["date_worker_start"] is DBNull))
                        q.ProcessingTime = (float)Convert.ToDateTime(r["date_sent"]).Subtract(Convert.ToDateTime(r["date_worker_start"])).TotalSeconds;

                    if (q.ProcessingTime > 15)
                        continue;

                    //if (q.Steps < 20 || q.Steps > 30)
                    //    continue;

                    Program.Data.Add(q);
                    count++;
                }
            }

            Console.WriteLine("Done. " + count);

            foreach (var item in Program.Data)
            {
                var properties = item.GetType().GetProperties();
                foreach (var property in properties)
                {
                    if (property.PropertyType == typeof(float))
                    {
                        var value = (float)property.GetValue(item);
                        if (value < 0)
                        {
                            Console.WriteLine($"Negative value found in {property.Name}: {value}");
                        }
                    }
                }
            }

            //var mlContext = new MLContext();


            //// Load the trained model
            //DataViewSchema modelSchema;
            //ITransformer model = mlContext.Model.Load("../model.zip", out modelSchema);

            //// Select a deterministic, random subset of a few thousand rows (e.g., 2,000 rows)
            //// Shuffle the data deterministically
            //List<RequestData> shuffledData = Data.OrderBy(x => rng.Next()).ToList();

            //// Take a subset of 2000 rows
            //int subsetSize = 2000;
            //List<RequestData> subset = shuffledData.Take(subsetSize).ToList();










            var mlContext = new MLContext();

            IDataView trainingData = mlContext.Data.LoadFromEnumerable(Program.Data);

            // Define AutoML experiment settings
            var experimentSettings = new RegressionExperimentSettings
            {
                MaxExperimentTimeInSeconds = 600, // Run for a maximum of 10 minutes
                OptimizingMetric = RegressionMetric.RSquared // Optimize for R-Squared, adjust based on your needs
            };

            // Create a regression experiment
            var experiment = mlContext.Auto().CreateRegressionExperiment(experimentSettings);

            // Execute the experiment. The progress handler will report progress.
            Console.WriteLine("Running AutoML regression experiment...");
            var result = experiment.Execute(trainingData, labelColumnName: nameof(RequestData.ProcessingTime), progressHandler: new RegressionExperimentProgressHandler());

            // Print experiment result
            var bestRun = result.BestRun;
            Console.WriteLine($"Best algorithm: {bestRun.TrainerName}");
            Console.WriteLine($"Metrics: R-Squared: {bestRun.ValidationMetrics.RSquared}, MAE: {bestRun.ValidationMetrics.MeanAbsoluteError}");

            // You can save the best model
            mlContext.Model.Save(bestRun.Model, trainingData.Schema, "BestModel.zip");

            mlContext.Log += (sender, e) =>
            {
                if (e.Kind == Microsoft.ML.Runtime.ChannelMessageKind.Info)
                Console.WriteLine($"[{e.Kind}] {e.Message}");
            };


            //            IDataView testData = mlContext.Data.LoadFromEnumerable(Data);

            //            // Compute Permutation Feature Importance
            //            // Assuming 'model' and 'testData' are correctly defined as before
            //            var permutationMetrics = mlContext.Regression.PermutationFeatureImportance(model, testData, labelColumnName: nameof(RequestData.ProcessingTime));

            //            // The feature names should correspond to those in your dataset before any transformations
            //            // Adjust featureNames to match the actual input feature names used during model training
            //            string[] featureNames = {
            //    "Model",
            //    "WorkerId",
            //    "Prompt",
            //    "NegativePrompt",
            //    "Width",
            //    "Height",
            //    "Steps",
            //    "CFGScale",
            //    "Type",
            //    "StartTimeHour",
            //    "DayOfWeek",
            //    "IsWorkerIdKnown" // Include original feature names as used before any transformation
            //};

            //            // Output the Feature Importance results
            //            Console.WriteLine("Feature Importances:");
            //            foreach (var featureName in featureNames)
            //            {
            //                if (permutationMetrics.ContainsKey(featureName))
            //                {
            //                    var metrics2 = permutationMetrics[featureName];
            //                    Console.WriteLine($"{featureName}: Mean Change in RSquared: {metrics2.RSquared.Mean:G4}");
            //                }
            //                else
            //                {
            //                    Console.WriteLine($"{featureName}: Feature importance not found");
            //                }
            //            }


            //            // Distribution of Steps
            //            var minSteps = Data.Min(d => d.Steps);
            //            var maxSteps = Data.Max(d => d.Steps);
            //            var averageSteps = Data.Average(d => d.Steps);
            //            var medianSteps = Data.OrderBy(d => d.Steps).ElementAt(Data.Count / 2).Steps; // Simplified median for demonstration

            //            Console.WriteLine($"Min Steps: {minSteps}");
            //            Console.WriteLine($"Max Steps: {maxSteps}");
            //            Console.WriteLine($"Average Steps: {averageSteps}");
            //            Console.WriteLine($"Median Steps: {medianSteps}");

            //            // Correlation between 'Steps' and 'ProcessingTime'
            //            var steps = Data.Select(d => d.Steps).ToList();
            //            var processingTime = Data.Select(d => d.ProcessingTime).ToList();

            //            double avgSteps = steps.Average();
            //            double avgProcessingTime = processingTime.Average();

            //            double sumProductMean = steps.Zip(processingTime, (s, p) => (s - avgSteps) * (p - avgProcessingTime)).Sum();
            //            double sumSqSteps = steps.Sum(s => Math.Pow(s - avgSteps, 2));
            //            double sumSqProcessingTime = processingTime.Sum(p => Math.Pow(p - avgProcessingTime, 2));

            //            double correlation = sumProductMean / Math.Sqrt(sumSqSteps * sumSqProcessingTime);

            //            Console.WriteLine($"Correlation between Steps and Processing Time: {correlation}");


            DataViewSchema modelSchema;
            ITransformer model = mlContext.Model.Load("BestModel.zip", out modelSchema);

            var transformedTestData = model.Transform(trainingData);

            // Run Permutation Feature Importance

            var permutationMetrics = mlContext.Regression.PermutationFeatureImportance(model, transformedTestData, labelColumnName: nameof(RequestData.ProcessingTime), permutationCount: 2);

            Console.WriteLine("Feature Importances:");

            // Given the concatenation order in your pipeline, features are mapped directly as they appear.
            //Console.WriteLine($"ModelEncoded: {permutationMetrics[0].RSquared.Mean:G4}");
            //Console.WriteLine($"WorkerEncoded: {permutationMetrics[1].RSquared.Mean:G4}");
            //Console.WriteLine($"PromptFeaturized: {permutationMetrics[2].RSquared.Mean:G4}");
            //Console.WriteLine($"NegativePromptFeaturized: {permutationMetrics[3].RSquared.Mean:G4}");
            //Console.WriteLine($"Width: {permutationMetrics[4].RSquared.Mean:G4}");
            //Console.WriteLine($"Height: {permutationMetrics[5].RSquared.Mean:G4}");
            //Console.WriteLine($"NormalizedSteps: {permutationMetrics[6].RSquared.Mean:G4}"); // Assuming 'Steps' gets normalized to 'NormalizedSteps'
            //Console.WriteLine($"CFGScale: {permutationMetrics[7].RSquared.Mean:G4}");
            //Console.WriteLine($"TypeConverted: {permutationMetrics[8].RSquared.Mean:G4}");
            //Console.WriteLine($"StartTimeHour: {permutationMetrics[9].RSquared.Mean:G4}");
            //Console.WriteLine($"DayOfWeek: {permutationMetrics[10].RSquared.Mean:G4}");
            //Console.WriteLine($"Steps: {permutationMetrics[11].RSquared.Mean:G4}"); // Direct inclusion of 'Steps', as listed in the concatenation

            // Note: The feature 'IsWorkerIdKnown' is not included in 'Features' vector for PFI analysis, hence not listed here.
        }

        public class RegressionExperimentProgressHandler : IProgress<RunDetail<RegressionMetrics>>
        {
            public void Report(RunDetail<RegressionMetrics> detail)
            {
                if (detail.Exception != null)
                {
                    Console.WriteLine($"Exception during AutoML iteration: {detail.Exception}");
                }
                else
                {
                    Console.WriteLine($"Iteration: {detail.RuntimeInSeconds}, TrainerName: {detail.TrainerName}, RSquared: {detail.ValidationMetrics?.RSquared}, MAE: {detail.ValidationMetrics?.MeanAbsoluteError}");
                }
            }
        }
    }
}
