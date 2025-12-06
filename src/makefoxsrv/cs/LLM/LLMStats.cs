using Newtonsoft.Json.Linq;
using Stripe;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WTelegram;

namespace makefoxsrv
{
    internal class LLMStats
    {
        [DbColumn("user_id")]
        public long userId { get; set; }

        [DbColumn("created_at")]
        public DateTime createdAt { get; set; }

        [DbColumn("input_tokens")]
        public int inputTokenCount { get; set; }

        [DbColumn("output_tokens")]
        public int outputTokenCount { get; set; }

        [DbColumn("total_tokens")]
        public int totalTokenCount { get; set; }

        [DbColumn("model")]
        public string model { get; set; } = string.Empty;

        [DbColumn("provider")]
        public string provider { get; set; } = string.Empty;

        [DbColumn("gen_id")]
        public string? genId { get; set; }

        [DbColumn("is_free")]
        public bool isFree { get; set; } = false;

        [DbColumn("error_str")]
        public string? errorStr { get; set; }

        [DbColumn("reasoning_tokens")]
        public int? reasoningTokens { get; set; }

        [DbColumn("image_tokens")]
        public int? imageTokens { get; set; }

        [DbColumn("cached_tokens")]
        public int? cachedTokens { get; set; }

        [DbColumn("num_media_prompt")]
        public int? numMediaPrompt { get; set; }

        [DbColumn("num_audio_prompt")]
        public int? numAudioPrompt { get; set; }

        [DbColumn("num_media_completion")]
        public int? numMediaCompletion { get; set; }

        [DbColumn("prompt_cost")]
        public decimal? promptCost { get; set; }

        [DbColumn("output_cost")]
        public decimal? outputCost { get; set; }

        [DbColumn("total_cost")]
        public decimal? totalCost { get; set; }

        [DbColumn("usage_cost")]
        public decimal? usageCost { get; set; }

        [DbColumn("cache_cost")]
        public decimal? cacheCost { get; set; }

        [DbColumn("finish_reason")]
        public string? finishReason { get; set; }

        [DbColumn("native_finish_reason")]
        public string? nativeFinishReason { get; set; }

        [DbColumn("latency")]
        public int? latency { get; set; }

        [DbColumn("generation_time")]
        public int? generationTime { get; set; }

        public async Task Save()
        {
            await FoxDB.SaveObjectAsync<LLMStats>(this, "llm_stats", "gen_id");
        }

        public async Task FetchStatsFromApi()
        {
            // Send the POST request

            if (genId is null)
                throw new Exception("genId cannot be null");

            await Task.Delay(3000); // Wait for 3 seconds to ensure the generation is processed

            var url = $"generation?id={Uri.EscapeDataString(genId)}";
            var response = await FoxLLM.HttpGetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();


            // Check if the request was successful
            if (response.IsSuccessStatusCode)
            {
                // Read and return the response content

                JObject jsonResponse = JObject.Parse(responseContent);

                //Console.WriteLine(jsonResponse.ToString(Newtonsoft.Json.Formatting.Indented));

                numMediaPrompt = jsonResponse["data"]?["num_media_prompt"]?.Value<int?>();
                numAudioPrompt = jsonResponse["data"]?["num_input_audio_prompt"]?.Value<int?>();
                numMediaCompletion = jsonResponse["data"]?["num_media_completion"]?.Value<int?>();
                
                generationTime = jsonResponse["data"]?["generation_time"]?.Value<int?>();
                latency = jsonResponse["data"]?["latency"]?.Value<int?>();

                cacheCost = -jsonResponse["data"]?["cache_discount"]?.Value<decimal?>();
                usageCost = jsonResponse["data"]?["usage"]?.Value<decimal?>();

                await this.Save();
            }
        }
    }
}