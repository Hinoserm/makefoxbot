#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading.Tasks;
using MySqlConnector;
using Tiktoken;
using TL;
using System.Data;

namespace makefoxsrv
{
    public static class FoxLLMConversation
    {
        // ===============================
        // Nested chat role + message types
        // ===============================

        public enum ChatRole
        {
            System,
            User,
            Assistant,
            Tool,
            Function
        }

        // Only applies to ChatRole enum
        private sealed class LowercaseEnumConverter : StringEnumConverter
        {
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                if (value is ChatRole role)
                    writer.WriteValue(role.ToString().ToLowerInvariant());
                else
                    base.WriteJson(writer, value, serializer);
            }
        }

        public class ChatMessage
        {
            [JsonProperty("role")]
            [JsonConverter(typeof(LowercaseEnumConverter))]
            public ChatRole Role { get; set; }

            [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
            public string? ToolCallId { get; set; }

            [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
            public string? Name { get; set; }

            // Normal text/image content
            [JsonProperty("content")]
            public object? JsonContent
            {
                get
                {
                    if (Role == ChatRole.Tool)
                    {
                        // xAI: content must be a JSON STRING, not a structured object
                        string serialized = ToolResult switch
                        {
                            null => "{}",
                            string s => s,  // if it’s already JSON text, keep it
                            _ => JsonConvert.SerializeObject(ToolResult, Formatting.None)
                        };
                        return serialized;
                    }

                    if (ImageData == null || ImageData.Length == 0)
                        return Content;

                    var parts = new List<object>();

                    if (!string.IsNullOrWhiteSpace(Content))
                        parts.Add(new { type = "text", text = Content });

                    parts.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:image/png;base64,{Convert.ToBase64String(ImageData)}" }
                    });

                    return parts;
                }
            }

            [JsonIgnore]
            public string? Content { get; set; }

            [JsonIgnore]
            public byte[]? ImageData { get; set; }

            [JsonIgnore]
            public object? ToolArguments { get; set; }

            [JsonIgnore]
            public object? ToolResult { get; set; }

            [JsonIgnore]
            public DateTime Date { get; set; } = DateTime.Now;

            public ChatMessage(
                ChatRole role,
                string? content = null,
                string? name = null,
                byte[]? imageData = null,
                DateTime? date = null)
            {
                Role = role;
                Content = content;
                Name = name;
                ImageData = imageData;
                Date = date ?? DateTime.Now;
            }

            public static ChatMessage ToolMessage(
                string? toolCallId,
                string name,
                string? args,
                string? result,
                DateTime? date = null)
            {
                return new ChatMessage(ChatRole.Tool, name: name, date: date)
                {
                    ToolCallId = toolCallId ?? Guid.NewGuid().ToString("N"),
                    ToolArguments = args,
                    ToolResult = result
                };
            }

            public int GetTokenCount(Tiktoken.Encoder encoder)
            {
                string raw = Content ?? JsonConvert.SerializeObject(JsonContent);
                return encoder.CountTokens(raw);
            }

            public override string ToString()
            {
                string preview = Content ?? (ToolArguments != null ? "[tool]" : "[image]");
                return $"[{Date:yyyy-MM-dd HH:mm:ss}] {Role}: {preview}";
            }
        }


        // ===============================
        // Core conversation assembly logic
        // ===============================

        public static async Task<List<ChatMessage>> FetchConversationAsync(FoxUser user, int maxTokens)
        {
            var messages = new List<ChatMessage>();

            int memoryBudget = (int)(maxTokens * 0.70);
            int convoBudget = maxTokens - memoryBudget;

            var encoder = ModelToEncoder.For("gpt-4o");
            var memoryPrompt = await BuildMemoryPromptAsync(user, memoryBudget, encoder);
            int memoryPromptTokens = memoryPrompt == null ? 0 : memoryPrompt.GetTokenCount(encoder);

            int memoryUsed = memoryPromptTokens;
            int memoryLeftover = memoryBudget - memoryUsed;
            convoBudget += Math.Max(0, memoryLeftover);

            List<ChatMessage> results = new();

            var llmSettings = await FoxLLMUserSettings.GetSettingsAsync(user);

            var historyDate = llmSettings.HistoryStartDate;

            results.AddRange(await FetchConversationMessagesAsync(user, convoBudget, historyDate, encoder));
            results.AddRange(await FetchFunctionCallHistoryAsync(user, convoBudget, historyDate, encoder));

            var imageMessages = new List<ChatMessage>();

            await using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync();
                var cmd = new MySqlCommand(@"
                    SELECT id, type, date_added
                    FROM images
                    WHERE user_id = @uid AND
                          date_added >= @hdate AND
                          hidden = 0 AND
                          flagged = 0 AND
                          (type = 'INPUT' OR type = 'OUTPUT')
                    ORDER BY date_added DESC
                    LIMIT 4;
                ", conn);

                cmd.Parameters.AddWithValue("@uid", user.UID);
                cmd.Parameters.AddWithValue("@hdate", historyDate);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    try
                    {
                        ulong id = reader.GetUInt64("id");
                        string type = reader.GetString("type");
                        DateTime createdAt = reader.GetDateTime("date_added");

                        var image = await FoxImage.Load(id);
                        if (image == null)
                            continue;

                        var jpeg = image.GetImageAsJpeg(60, 1280);
                        if (jpeg == null || jpeg.Length == 0)
                            continue;

                        var tags = await image.GetImageTagsAsync();
                        string tagList = tags != null && tags.Count > 0
                            ? string.Join(", ", tags.Keys)
                            : string.Empty;

                        var msgRole = type.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase)
                            ? ChatRole.Assistant
                            : ChatRole.System;

                        var chatMsg = new ChatMessage(msgRole,
                            $"[SYSTEM] The user uploaded this image. Tags: {tagList}",
                            imageData: jpeg,
                            date: createdAt);

                        imageMessages.Add(chatMsg);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex);
                    }
                }
            }

            results.AddRange(imageMessages);

            results = results.OrderBy(m => m.Date).ToList();

            var finalConversation = new List<ChatMessage>();
            DateTime? lastTs = null;

            foreach (var msg in results)
            {
                bool needTimestamp = !lastTs.HasValue || (msg.Date - lastTs.Value) >= TimeSpan.FromMinutes(10);
                if (needTimestamp)
                {
                    var timeLabel = $"Timestamp: {msg.Date:HH:mm, yyyy-MM-dd}";
                    convoBudget -= encoder.CountTokens(timeLabel);
                    finalConversation.Add(new ChatMessage(ChatRole.System, timeLabel, date: msg.Date));
                    lastTs = msg.Date;
                }

                int msgTokens = msg.GetTokenCount(encoder);
                finalConversation.Add(msg);
                convoBudget -= msgTokens;
            }

            if (memoryPrompt != null)
            {
                if (memoryPromptTokens <= memoryBudget)
                    finalConversation.Add(memoryPrompt);
                else
                {
                    FoxLog.WriteLine($"[Warning] forcibly adding memory prompt (over by {memoryPromptTokens - memoryBudget} tokens).");
                    finalConversation.Add(memoryPrompt);
                }
            }

            return finalConversation;
        }

        private static async Task<List<ChatMessage>> FetchConversationMessagesAsync(
            FoxUser user,
            int convoBudget,
            DateTime historyDate,
            Encoder encoder)
        {
            var result = new List<ChatMessage>();
            int usedTokens = 0;

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var sql = @"
        SELECT c.role, c.content, c.created_at
        FROM llm_conversations c
        WHERE c.user_id = @uid
          AND c.deleted = 0
          AND c.created_at >= @hdate
        ORDER BY c.created_at DESC
        LIMIT 2000;
    ";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@hdate", historyDate);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string roleStr = reader.GetString("role");
                string content = reader.GetString("content");
                DateTime createdAt = reader.GetDateTime("created_at");

                int needed = encoder.CountTokens(content);
                if (usedTokens + needed > convoBudget)
                    break;

                if (!Enum.TryParse(roleStr, true, out ChatRole role))
                    role = ChatRole.System;

                result.Add(new ChatMessage(role, content, date: createdAt));
                usedTokens += needed;
            }

            return result;
        }

        private static async Task<List<ChatMessage>> FetchFunctionCallHistoryAsync(
            FoxUser user,
            int convoBudget,
            DateTime historyDate,
            Encoder encoder)
        {
            var result = new List<ChatMessage>();
            int usedTokens = 0;

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT f.call_id, f.function_name, f.parameters, f.return_results, f.created_at
                FROM llm_function_calls f
                WHERE f.user_id = @uid
                  AND f.created_at >= @hdate
                ORDER BY f.created_at DESC
                LIMIT 2000;
            ";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@hdate", historyDate);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string functionName = reader.GetString("function_name");
                string rawParamsJson = reader.GetString("parameters");
                string? returnResults = reader.IsDBNull("return_results")
                    ? null
                    : reader.GetString("return_results");
                DateTime createdAt = reader.GetDateTime("created_at");
                string? callId = reader.IsDBNull("call_id") ? null : reader.GetString("call_id");



                var toolMessage = ChatMessage.ToolMessage(
                    callId,
                    functionName,
                    rawParamsJson,
                    returnResults,
                    createdAt
                );

                int needed = encoder.CountTokens(JsonConvert.SerializeObject(toolMessage.JsonContent));
                if (usedTokens + needed > convoBudget)
                    break;

                result.Add(toolMessage);
                usedTokens += needed;
            }

            result.Reverse();
            return result;
        }

        public static async Task<long> SaveFunctionCallAsync(
            FoxUser user,
            string? callId,
            string functionName,
            string parametersJson,
            string? returnResultsJson = null,
            long? finalId = null)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("Function name cannot be null or empty.", nameof(functionName));

            if (string.IsNullOrWhiteSpace(parametersJson))
                parametersJson = "{}";

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO llm_function_calls
                    (call_id, user_id, function_name, parameters, return_results, created_at, final_id)
                VALUES
                    (@call_id, @user_id, @function_name, @parameters, @return_results, @created_at, @final_id);
                SELECT LAST_INSERT_ID();
            ";

            await using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@call_id", callId);
            cmd.Parameters.AddWithValue("@user_id", user.UID);
            cmd.Parameters.AddWithValue("@function_name", functionName);
            cmd.Parameters.AddWithValue("@parameters", parametersJson);
            cmd.Parameters.AddWithValue("@return_results", returnResultsJson);
            cmd.Parameters.AddWithValue("@created_at", DateTime.Now);
            cmd.Parameters.AddWithValue("@final_id", (object?)finalId ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }





        private static string FormatFunctionCall(string functionName, string rawParamsJson)
        {
            var lines = new List<string> { $"You (the AI) called function {functionName} with the parameters:" };
            try
            {
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(rawParamsJson);
                if (obj != null)
                {
                    foreach (var prop in obj.Properties())
                        lines.Add($"- {prop.Name}: {prop.Value?.ToString() ?? ""}");
                }
                else
                    lines.Add($"(Unable to parse parameters JSON. Raw: {rawParamsJson})");
            }
            catch (Exception ex)
            {
                lines.Add($"(Error parsing JSON: {ex.Message} / Raw: {rawParamsJson})");
            }

            return string.Join("\n", lines);
        }

        private static async Task<ChatMessage?> BuildMemoryPromptAsync(
            FoxUser user,
            int memoryBudget,
            Encoder encoder)
        {
            var lines = new List<string>();

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                SELECT e.text AS memory_text
                FROM llm_loaded_memories lm
                JOIN llm_embeddings e ON lm.memory_id = e.id
                WHERE lm.user_id = @uid
                ORDER BY lm.last_used_at DESC;
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);

            int usedTokens = 0;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string memText = reader.GetString("memory_text");
                var bullet = "\n- " + memText;
                int bulletToks = encoder.CountTokens(bullet);

                if (usedTokens + bulletToks > memoryBudget)
                    break;

                lines.Add(bullet);
                usedTokens += bulletToks;
            }

            if (lines.Count == 0)
                return null;

            const string header = "The system found these relevant memories:";
            int headerTokens = encoder.CountTokens(header);
            if (headerTokens + usedTokens > memoryBudget)
                return null;

            var finalText = header + string.Join("", lines);
            return new ChatMessage(ChatRole.System, finalText);
        }

        // ===============================
        // DB utility methods
        // ===============================

        public static async Task<long> InsertConversationMessageAsync(FoxUser user, ChatRole role, string content, TL.Message? message)
        {
            var logDir = Path.Combine("..", "logs", "llm");
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"{user.UID}.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var prefix = $"[{timestamp}] ({role.ToString().ToUpperInvariant()}) ";

            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var formatted = new System.Text.StringBuilder();
            foreach (var line in lines)
                formatted.Append(prefix).Append(line).Append("\r\n");

            await File.AppendAllTextAsync(logFile, formatted.ToString(), System.Text.Encoding.UTF8);

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_conversations (user_id, role, content, tg_msgid, created_at)
                VALUES (@uid, @role, @ct, @msgid, @now);
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@ct", content);
            cmd.Parameters.AddWithValue("@msgid", message?.ID);
            cmd.Parameters.AddWithValue("@now", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();
            return cmd.LastInsertedId;
        }

        public static async Task DeleteConversationMessageAsync(long messageId)
        {
            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand("UPDATE llm_conversations SET deleted = 1 WHERE id = @msgId;", conn);
            cmd.Parameters.AddWithValue("@msgId", messageId);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task DeleteConversationTelegramMessagesAsync(int[] messageIds, int batchSize = 1000)
        {
            if (messageIds == null || messageIds.Length == 0)
                return;

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            for (int i = 0; i < messageIds.Length; i += batchSize)
            {
                var batch = messageIds.Skip(i).Take(batchSize).ToArray();
                var placeholders = string.Join(',', batch.Select((_, idx) => $"@msg{idx}"));

                var cmd = new MySqlCommand($@"
                    UPDATE llm_conversations 
                    SET deleted = 1 
                    WHERE tg_msgid IN ({placeholders});
                ", conn);

                for (int j = 0; j < batch.Length; j++)
                    cmd.Parameters.AddWithValue($"@msg{j}", batch[j]);

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
