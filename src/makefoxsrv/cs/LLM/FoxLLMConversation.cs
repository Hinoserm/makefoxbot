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
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

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

            [JsonIgnore]
            private int? _tokenCount = null;

            public ChatMessage(
                ChatRole role,
                string? content = null,
                string? name = null,
                byte[]? imageData = null,
                DateTime? date = null,
                int? tokenCount = null)
            {
                Role = role;
                Content = content;
                Name = name;
                ImageData = imageData;
                _tokenCount = tokenCount;
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
                if (_tokenCount.HasValue)
                    return _tokenCount.Value;

                string raw = Content ?? JsonConvert.SerializeObject(JsonContent);

                _tokenCount = encoder.CountTokens(raw);

                return _tokenCount.Value;
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

        public static async Task<List<ChatMessage>> FetchConversationAsync(FoxUser user, int maxTokens, int recursionsAllowed = 3)
        {
            var messages = new List<ChatMessage>();

            //var memoryPrompt = await BuildMemoryPromptAsync(user, encoder);
            //int memoryPromptTokens = memoryPrompt == null ? 0 : memoryPrompt.GetTokenCount(encoder);

            List<ChatMessage> results = new();

            var llmSettings = await FoxLLMUserSettings.GetSettingsAsync(user);

            var historyDate = llmSettings.HistoryStartDate;

            results.AddRange(await FetchConversationMessagesAsync(user, historyDate));
            results.AddRange(await FetchFunctionCallHistoryAsync(user, historyDate));

            var imageMessages = new List<ChatMessage>();

            await using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync();
                var cmd = new MySqlCommand(@"
                    SELECT i.id, i.type, i.date_added
                    FROM images i
                    LEFT JOIN images_tg_info ti ON ti.id = i.id
                    WHERE i.user_id = @uid
                      AND i.date_added >= @hdate
                      AND i.hidden = 0
                      AND i.flagged = 0
                      AND (i.type = 'INPUT' OR i.type = 'OUTPUT')
                      AND (ti.telegram_chatid IS NULL)
                    ORDER BY i.date_added DESC
                    LIMIT 10;
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
                            $"The user uploaded this image. Tags: {tagList}",
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
                    finalConversation.Add(new ChatMessage(ChatRole.System, timeLabel, date: msg.Date));
                    lastTs = msg.Date;
                }

                finalConversation.Add(msg);
            }

            // Enforce maxTokens limit by trimming oldest messages
            int totalTokens = 0;
            var encoder = ModelToEncoder.For("gpt-4o");
            var kept = new List<ChatMessage>();

            // Walk backward (newest to oldest)
            for (int i = results.Count - 1; i >= 0; i--)
            {
                var msg = results[i];
                int tok = msg.GetTokenCount(encoder);

                if (totalTokens + tok > maxTokens)
                {
                    if (recursionsAllowed > 0)
                    {
                        try
                        {
                            FoxLog.WriteLine($"[FetchConversation] Exceeded max tokens ({totalTokens + tok}/{maxTokens}), condensing and retrying...");
                            await CondenseConversationAsync(user, keepRecentTokens: 2000);
                            return await FetchConversationAsync(user, maxTokens, recursionsAllowed - 1);
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex);
                        }
                    }
                    break;
                }

                totalTokens += tok;
                kept.Add(msg);
            }

            // We built from newest to oldest, so reverse back
            kept.Reverse();
            results = kept;

            FoxLog.WriteLine($"[FetchConversation] Trimmed to {results.Count} messages ({totalTokens}/{maxTokens} tokens)");

            return finalConversation;
        }

        private static async Task<List<ChatMessage>> FetchConversationMessagesAsync(
            FoxUser user,
            DateTime historyDate)
        {
            var result = new List<ChatMessage>();

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT c.role, c.content, c.created_at, c.token_count
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
                int? tokenCount = reader.IsDBNull("token_count")
                    ? (int?)null
                    : reader.GetInt32("token_count");

                if (!Enum.TryParse(roleStr, true, out ChatRole role))
                    role = ChatRole.System;

                result.Add(new ChatMessage(role, content, date: createdAt, tokenCount: tokenCount));
            }

            return result;
        }

        private static async Task<List<ChatMessage>> FetchFunctionCallHistoryAsync(
            FoxUser user,
            DateTime historyDate)
        {
            var result = new List<ChatMessage>();

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

                if (functionName != "SendResponse")
                {
                    var sysPromptStr = $"You called {functionName} with the following parameters:\r\n\r\n{rawParamsJson}\r\n\r\nDo not repeat this raw data to the user.";

                    result.Add(new ChatMessage(ChatRole.System, sysPromptStr, date: createdAt));
                }

                var toolMessage = ChatMessage.ToolMessage(
                    callId,
                    functionName,
                    rawParamsJson,
                    returnResults,
                    createdAt.AddMilliseconds(1)
                );

                //int needed = encoder.CountTokens(JsonConvert.SerializeObject(toolMessage.JsonContent));

                result.Add(toolMessage);
            }

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

        public static async Task CondenseConversationAsync(FoxUser user, int keepRecentTokens = 2000)
        {
            var encoder = ModelToEncoder.For("gpt-4o");

            // 1. Fetch full history (up to ~50k tokens)
            var fullHistory = await FetchConversationAsync(user, 30000, 0);
            if (fullHistory.Count == 0)
                return;

            // 2. Determine how many messages make up the recent window
            int running = 0;
            var recentMessages = new List<ChatMessage>();

            for (int i = fullHistory.Count - 1; i >= 0; i--)
            {
                var msg = fullHistory[i];
                int toks = msg.GetTokenCount(encoder);

                if (running + toks > keepRecentTokens)
                    break;

                running += toks;
                recentMessages.Add(msg);
            }

            if (recentMessages.Count == 0)
            {
                // Avoid edge case: if keepRecentTokens is too small, keep at least one message
                recentMessages.Add(fullHistory.Last());
            }

            recentMessages.Reverse();
            DateTime oldestRecentDate = recentMessages.First().Date;

            // 3. Select the rest for condensation (older than oldest recent)
            var oldMessages = fullHistory.Where(m => m.Date < oldestRecentDate).ToList();
            if (oldMessages.Count == 0)
            {
                FoxLog.WriteLine($"[CondenseConversation] Nothing to condense for user {user.UID}.");
                return;
            }

            // 4. Build summarization prompt
            var sysPrompt = new StringBuilder();
            sysPrompt.AppendLine("You are a conversation summarizer.");
            sysPrompt.AppendLine("Condense the provided chat history into a concise, factual list of system messages.");
            sysPrompt.AppendLine("Each message should summarize one or more logically connected exchanges, preserving meaning, decisions, and emotional tone.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("Output format:");
            sysPrompt.AppendLine("- Return a JSON array of strings.");
            sysPrompt.AppendLine("- Each string must begin with the timestamp of the first relevant message, e.g. '[2025-10-15 17:51] User asked about storage; you explained caching.'");
            sysPrompt.AppendLine("- Merge adjacent messages within ~5 minutes where appropriate.");
            sysPrompt.AppendLine("- Maintain strict chronological order.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("Guidelines:");
            sysPrompt.AppendLine("- Use 'User' for the human participant, 'You' for the AI, and 'System' for automatic or background actions.");
            sysPrompt.AppendLine("- Be concise but accurate; merge redundant exchanges into single summaries.");
            sysPrompt.AppendLine("- You may include brief image descriptions (e.g. 'User uploaded an image of a red fox in lab; you described it as experimental.').");
            sysPrompt.AppendLine("- Avoid commentary or role labels like 'assistant'/'user'.");
            sysPrompt.AppendLine("- You may condense multiple related messages into one line when appropriate.");
            sysPrompt.AppendLine("- Include actual timestamps as provided.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("Examples:");
            sysPrompt.AppendLine(" - [2025-10-15 17:51] User greeted you; you introduced yourself as The Professor.");
            sysPrompt.AppendLine(" - [2025-10-15 17:54] User uploaded image of neon fox; you compared it to a 1970s disco scene.");
            sysPrompt.AppendLine(" - [2025-10-15 18:03] System erased your memory; you rebooted and reintroduced yourself.");
            sysPrompt.AppendLine(" - [2025-10-15 18:47] User requested LORA search; you returned results and noted server limits.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("Compress total length by at least 70% but keep clarity, sequence, and timestamps.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("You must return only the JSON array of strings. No extra commentary, no additional text.");

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, sysPrompt.ToString())
            };

            foreach (var msg in oldMessages)
            {
                string timestamp = msg.Date.ToString("yyyy-MM-dd HH:mm");
                string formatted = $"[{timestamp}] {msg.Role}: {msg.Content}";
                messages.Add(new(ChatRole.System, formatted));
            }

            // 5. Send request to model
            var request = new
            {
                model = "x-ai/grok-4-fast",
                user = $"CONDENSE:{user.UID}",
                reasoning = new { enabled = false },
                max_tokens = 20000,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "condensed_conversation",
                        strict = true,
                        schema = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "string",
                                description = "A single system message summarizing multiple parts of the conversation."
                            }
                        }
                    }
                },
                messages
            };

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.llmApiKey);

                var json = JsonConvert.SerializeObject(request, Formatting.None);
                var resp = await http.PostAsync("https://openrouter.ai/api/v1/chat/completions",
                    new StringContent(json, Encoding.UTF8, "application/json"));
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"CondenseConversation failed: {resp.StatusCode}\n{content}");

                var parsed = JObject.Parse(content);
                var condensedJson = parsed["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(condensedJson))
                    throw new Exception("LLM returned empty condensation result.");

                var condensedList = JsonConvert.DeserializeObject<List<string>>(condensedJson)
                                    ?? throw new Exception("Failed to parse condensed result.");

                // 6. Clear old history (everything before oldest recent date)
                var llmSettings = await FoxLLMUserSettings.GetSettingsAsync(user);
                await llmSettings.ClearHistoryAsync(oldestRecentDate);

                // 7. Insert condensed messages with timestamps slightly after cutoff
                DateTime insertDate = oldestRecentDate.AddMilliseconds(1);
                foreach (var msg in condensedList)
                {
                    await InsertConversationMessageAsync(
                        user,
                        ChatRole.System,
                        msg,
                        null,
                        insertDate
                    );

                    insertDate = insertDate.AddMilliseconds(1);
                }

                FoxLog.WriteLine($"[CondenseConversation] Condensed {oldMessages.Count} old messages → {condensedList.Count} summaries for user {user.UID}");
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
            }
        }

        private static async Task<ChatMessage?> BuildMemoryPromptAsync(
            FoxUser user,
            Tiktoken.Encoder encoder)
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

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string memText = reader.GetString("memory_text");
                var bullet = "\n- " + memText;

                lines.Add(bullet);
            }

            if (lines.Count == 0)
                return null;

            const string header = "The system found these relevant memories:";

            var finalText = header + string.Join("", lines);
            return new ChatMessage(ChatRole.System, finalText);
        }

        // ===============================
        // DB utility methods
        // ===============================

        public static async Task<long> InsertConversationMessageAsync(FoxUser user, ChatRole role, string content, TL.Message? message = null, DateTime? date = null)
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

            // Calculate token count from content string
            var encoder = ModelToEncoder.For("gpt-4o");
            int tokenCount = encoder.CountTokens(content);

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_conversations (user_id, role, content, tg_msgid, token_count, created_at)
                VALUES (@uid, @role, @ct, @msgid, @token_count, @now);
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@ct", content);
            cmd.Parameters.AddWithValue("@msgid", message?.ID);
            cmd.Parameters.AddWithValue("@token_count", tokenCount);
            cmd.Parameters.AddWithValue("@now", date ?? DateTime.Now);

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

        public static async Task<int> DeleteLastConversationMessagesAsync(FoxUser user, int count)
        {
            if (count <= 0)
                return 0;

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            // Select the latest N message IDs
            var selectSql = @"
                SELECT id
                FROM llm_conversations
                WHERE user_id = @uid
                  AND deleted = 0
                ORDER BY created_at DESC
                LIMIT @count;
            ";

            using var selectCmd = new MySqlCommand(selectSql, conn);
            selectCmd.Parameters.AddWithValue("@uid", user.UID);
            selectCmd.Parameters.AddWithValue("@count", count);

            var ids = new List<long>();
            using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    ids.Add(reader.GetInt64("id"));
            }

            if (ids.Count == 0)
                return 0;

            // Mark them deleted
            var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            var updateSql = $"UPDATE llm_conversations SET deleted = 1 WHERE id IN ({placeholders});";

            using var updateCmd = new MySqlCommand(updateSql, conn);
            for (int i = 0; i < ids.Count; i++)
                updateCmd.Parameters.AddWithValue($"@id{i}", ids[i]);

            await updateCmd.ExecuteNonQueryAsync();

            FoxLog.WriteLine($"[DeleteLastConversationMessages] Marked {ids.Count} most recent messages deleted for user {user.UID}");

            return ids.Count;
        }

    }
}
