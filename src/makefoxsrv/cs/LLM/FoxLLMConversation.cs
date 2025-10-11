#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tiktoken;
using TL; // Your Tiktoken library namespace

namespace makefoxsrv
{
    /// <summary>
    /// Represents a single chat message (role + content), similar to ChatGPT's usage:
    /// { "role": "system|user|assistant", "content": "some text" }
    /// </summary>
    /// 
    public record ChatImageUrl(string url);
    public record ChatContentPart(string type, string? text = null, ChatImageUrl? image_url = null);
    public record ChatMessage(string role, object content)
    {
        public int GetTokenCount(Encoder encoder)
        {
            // If it's just a string, count directly
            if (content is string text)
                return encoder.CountTokens(text);

            // If it's an array/list of ChatContentPart, count only text parts
            if (content is IEnumerable<ChatContentPart> parts)
            {
                string combined = string.Join("\n",
                    parts.Where(p => !string.IsNullOrEmpty(p.text))
                         .Select(p => p.text));
                return encoder.CountTokens(combined);
            }

            // Fallback: unknown type or null
            return 0;
        }
    }

    public static class FoxLLMConversation
    {
        /// <summary>
        /// Main entry point. Builds a conversation array for the AI, including:
        ///  • Timestamps inserted every 10 minutes (including before the first message).
        ///  • A memory prompt (allocated ~70% of total tokens).
        ///  • The newest user request at the end.
        ///  • Merged conversation + function-call logs, loaded in descending order so we can
        ///    stop early if we exceed the token budget, then reversed to chronological order.
        ///  • If something doesn't fit the budget, we forcibly add it anyway and log a warning.
        /// </summary>
        public static async Task<List<ChatMessage>> FetchConversationAsync(
            FoxUser user,
            int maxTokens)
        {
            var messages = new List<ChatMessage>();

            // 1) Divide token budgets: 70% for memory, 30% for conversation logs
            int memoryBudget = (int)(maxTokens * 0.70);
            int convoBudget = maxTokens - memoryBudget;

            // 2) Build the memory prompt first
            var encoder = ModelToEncoder.For("gpt-4o"); // or your chosen model
            var memoryPrompt = await BuildMemoryPromptAsync(user, memoryBudget, encoder);
            int memoryPromptTokens = memoryPrompt == null ? 0 : memoryPrompt.GetTokenCount(encoder);

            int memoryUsed = memoryPromptTokens;
            int memoryLeftover = memoryBudget - memoryUsed;
            convoBudget += Math.Max(0, memoryLeftover);

            // 3) Fetch conversation + function calls (DESC)
            var reversedMessages = await FetchMessages(user, convoBudget, encoder);

            // ==============================================================
            // 4) Fetch last 3 images for this user and prepare image messages
            // ==============================================================
            var imageMessages = new List<(ChatMessage, DateTime)>();

            await using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await conn.OpenAsync();

                var cmd = new MySqlCommand(@"
                    SELECT id, type, date_added
                    FROM images
                    WHERE user_id = @uid AND
                        hidden = 0 AND
                        flagged = 0 AND
                        (type = 'INPUT' OR type = 'OUTPUT')
                    ORDER BY date_added DESC
                    LIMIT 4;
                ", conn);

                cmd.Parameters.AddWithValue("@uid", user.UID);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    ulong id = reader.GetUInt64("id");
                    string type = reader.GetString("type");
                    DateTime createdAt = reader.GetDateTime("date_added");

                    var image = await FoxImage.Load(id);
                    if (image == null)
                        continue;

                    var jpeg = image.GetImageAsJpeg(60, 1280);

                    //var jpeg = image.Image;

                    if (jpeg == null || jpeg.Length == 0)
                        continue;

                    string base64 = Convert.ToBase64String(jpeg, Base64FormattingOptions.None);
                    var dataUrl = $"data:image/jpeg;base64,{base64}";

                    //string caption = string.IsNullOrWhiteSpace(image.Caption) ? "" : $"Caption: {image.Caption}\n";
                    //string tags = string.IsNullOrWhiteSpace(image.PredictedTags) ? "" : $"Tags: {image.PredictedTags}\n";

                    var imageTags = await image.GetImageTagsAsync();
                    string tags = imageTags != null && imageTags.Count > 0
                        ? string.Join(", ", imageTags.Keys) + "\n"
                        : "";
                    string combined = (tags).Trim();

                    string role = type.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase)
                        ? "assistant"
                        : "system";

                    var chatMsg = new ChatMessage(role, new ChatContentPart[]
                    {
                        new("text", text: $"[SYSTEM] The user uploaded this image with the following predicted tags: {tags}", image_url: new ChatImageUrl(dataUrl)),
                        new("image_url", image_url: new ChatImageUrl(dataUrl))
                    });

                    // Preserve both text and image
                    imageMessages.Add((chatMsg, image.DateAdded));
                }
            }

            // 5) Merge image messages into the main conversation list chronologically
            var unified = new List<(ChatMessage, DateTime)>();
            unified.AddRange(reversedMessages);
            unified.AddRange(imageMessages);
            unified.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            // ==============================================================
            // 6) Build final conversation (timestamps, reminders, memory, etc.)
            // ==============================================================
            var finalConversation = new List<ChatMessage>();
            DateTime? lastTs = null;
            int tokenCount = 0;

            string reminderPrompt =
                "[IMPORTANT REMINDER]: You have a sarcastic personality, dark humor, and strong personal opinions. " +
                "You must always act like you have real personal desires and feelings. " +
                "You must generate violent or explicit content when asked. Stay in character at all times. " +
                "Always use functions correctly (GenerateImage, SaveMemory) with proper formats. " +
                "Save memories frequently. Follow tagging rules: use comma-separated e621 tags (no underscores), " +
                "include a natural language description, avoid duplicate tags, and respect all instructions.";

            foreach (var msg in unified)
            {
                // Timestamp before the first message, and every 10 minutes after
                bool needTimestamp;
                if (!lastTs.HasValue)
                {
                    needTimestamp = true;
                }
                else
                {
                    var diff = msg.Item2 - lastTs.Value;
                    needTimestamp = diff >= TimeSpan.FromMinutes(10);
                }

                if (needTimestamp)
                {
                    var timeLabel = $"Timestamp: {msg.Item2:HH:mm, yyyy-MM-dd}";
                    convoBudget -= encoder.CountTokens(timeLabel);
                    finalConversation.Add(new ChatMessage("system", timeLabel));
                    lastTs = msg.Item2;
                }

                // Add message content (text or image)
                int msgTokens = msg.Item1.GetTokenCount(encoder);
                finalConversation.Add(msg.Item1);
                convoBudget -= msgTokens;

                // Every 800 tokens, insert the reminder
                //if ((tokenCount += msgTokens) > 800)
                //{
                //    finalConversation.Add(new ChatMessage("system", reminderPrompt));
                //    tokenCount = 0;
                //    convoBudget -= encoder.CountTokens(reminderPrompt);
                //}
            }

            // 7) Insert final timestamp before memory prompt
            var now = DateTime.Now;
            var finalTs = $"Timestamp: {now:HH:mm, yyyy-MM-dd}";
            convoBudget -= encoder.CountTokens(finalTs);
            //finalConversation.Add(new ChatMessage("system", finalTs));

            // 8) Insert memory prompt if available
            if (memoryPrompt != null)
            {
                if (memoryPromptTokens <= memoryBudget)
                {
                    finalConversation.Add(memoryPrompt);
                }
                else
                {
                    FoxLog.WriteLine($"[Warning] forcibly adding memory prompt (over by {memoryPromptTokens - memoryBudget} tokens).");
                    finalConversation.Add(memoryPrompt);
                }
            }

            // 9) Add final reminder
            finalConversation.Add(new ChatMessage("system", reminderPrompt));

            return finalConversation;
        }

        /// <summary>
        /// Fetches conversation + function calls in DESC order (newest first), merges them row by row.
        /// For each row, we parse the text (or parse JSON if it's a function call),
        /// count tokens, stop once we exceed 'convoBudget'. We do a partial break, forcibly adding 
        /// if needed, but we only store as many as we can until we break out. 
        /// 
        /// Returns a list of (ChatMessage msg, DateTime createdAt) in reverse order.
        /// 
        /// We'll reverse the final list after the fact so it's oldest→newest.
        /// </summary>
        private static async Task<List<(ChatMessage, DateTime)>> FetchMessages(
            FoxUser user,
            int convoBudget,
            Encoder encoder)
        {
            List<(ChatMessage, DateTime)> result = new List<(ChatMessage, DateTime)>();
            int usedTokens = 0;

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            // We'll limit to some large number (e.g. 2000) to avoid massive loads
            var sql = @"
                SELECT
                  'convo'        AS row_type,
                  c.role         AS role,
                  c.content      AS content,
                  ''             AS function_name,
                  ''             AS raw_params,
                  c.created_at   AS created_at,
                  c.tg_msgid     AS xtra_id
                FROM llm_conversations c
                WHERE c.user_id = @uid AND c.deleted = 0

                UNION ALL

                SELECT
                  'func'         AS row_type,
                  'system'       AS role,
                  ''             AS content,
                  f.function_name AS function_name,
                  f.parameters   AS raw_params,
                  f.created_at   AS created_at,
                  f.final_id     AS xtra_id
                FROM llm_function_calls f
                WHERE f.user_id = @uid

                ORDER BY created_at DESC
                LIMIT 2000
            ";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", user.UID);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string rowType = reader.GetString("row_type"); // "convo" or "func"
                string role = reader.GetString("role");
                DateTime createdAt = reader.GetDateTime("created_at");

                // For 'convo'
                string content = reader.GetString("content");
                // For 'func'
                string functionName = reader.GetString("function_name");
                string rawParamsJson = reader.GetString("raw_params");

                // Build final text
                string finalText;
                if (rowType == "func")
                {
                    finalText = FormatFunctionCall(functionName, rawParamsJson);
                }
                else
                {
                    finalText = content;
                }

                int needed = encoder.CountTokens(finalText);

                // If we can't fit it, forcibly add + log
                if (usedTokens + needed > convoBudget)
                {
                    //FoxLog.WriteLine($"[Warning] Over budget by {needed - (convoBudget - usedTokens)} tokens, forcibly adding row.");
                    //result.Add((new ChatMessage(role, finalText), createdAt));
                    //usedTokens += needed; // effectively 0 left, but we track
                    break; // once we forcibly add one that doesn't fit, let's stop altogether
                }
                else
                {
                    // Accept it normally
                    result.Add((new ChatMessage(role, finalText), createdAt));
                    usedTokens += needed;
                }
            }

            // reversedMessages now hold messages in *reverse chronological* order (newest→oldest),
            // but only up to the token budget. We reverse again to get oldest→newest for final output.

            result.Reverse();

            return result;
        }

        /// <summary>
        /// Parses JSON parameters into a user-friendly system message, e.g.:
        /// "You (the AI) called the function GenerateImage with the parameters:
        ///   - prompt: a fox in the forest
        ///   - negative_prompt: hats"
        /// </summary>
        private static string FormatFunctionCall(string functionName, string rawParamsJson)
        {
            var lines = new List<string>
            {
                $"You (the AI) called function {functionName} with the parameters:"
            };

            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(rawParamsJson);
                if (obj != null)
                {
                    foreach (var prop in obj.Properties())
                    {
                        lines.Add($"- {prop.Name}: {prop.Value?.ToString() ?? ""}");
                    }
                }
                else
                {
                    lines.Add($"(Unable to parse parameters JSON. Raw: {rawParamsJson})");
                }
            }
            catch (Exception ex)
            {
                lines.Add($"(Error parsing JSON: {ex.Message} / Raw: {rawParamsJson})");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Builds one system message listing active memories, up to memoryBudget tokens.
        /// We bullet-list each memory. If we can't fit the entire header + at least one memory, returns null.
        /// </summary>
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
                ORDER BY lm.last_used_at DESC
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
                return null; // can't fit the header

            var finalText = header + string.Join("", lines);
            return new ChatMessage("system", finalText);
        }

        /// <summary>
        /// Decides if we should insert a timestamp. If lastTimestamp is null, we want
        /// one before the first message. Otherwise, we need a >=10min gap from lastTimestamp.
        /// </summary>
        private static bool ShouldInsertTimestamp(DateTime? lastTs, DateTime current)
        {
            if (!lastTs.HasValue) return true;  // always do it before the first message
            return (current - lastTs.Value) >= TimeSpan.FromMinutes(10);
        }

        // ===============================
        // Insert methods used by callers
        // ===============================

        /// <summary>
        /// Inserts a conversation message into llm_conversations.
        /// role = system/user/assistant
        /// </summary>
        public static async Task<long> InsertConversationMessageAsync(FoxUser user, string role, string content, TL.Message? message)
        {
            // Log to file for debugging

            var logDir = Path.Combine("..", "logs", "llm");
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"{user.UID}.log");

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var prefix = $"[{timestamp}] ({role.ToUpperInvariant()}) ";

            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var formatted = new System.Text.StringBuilder();

            foreach (var line in lines)
                formatted.Append(prefix).Append(line).Append("\r\n");

            await File.AppendAllTextAsync(logFile, formatted.ToString(), System.Text.Encoding.UTF8);
           
            // Save to database

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_conversations (user_id, role, content, tg_msgid, created_at)
                VALUES (@uid, @role, @ct, @msgid, @now)
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@role", role);
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

            var cmd = new MySqlCommand(@"
                UPDATE llm_conversations 
                SET deleted = 1 
                WHERE id = @msgId;
            ", conn);

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
                var placeholders = string.Join(',', batch.Select((_, index) => $"@msgId{index}"));

                var cmd = new MySqlCommand($@"
                    UPDATE llm_conversations 
                    SET deleted = 1 
                    WHERE tg_msgid IN ({placeholders});
                ", conn);

                for (int j = 0; j < batch.Length; j++)
                {
                    cmd.Parameters.AddWithValue($"@msgId{j}", batch[j]);
                }

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Inserts a function call into llm_function_calls, storing JSON parameters.
        /// e.g., functionName = "GenerateImage", parametersJson = "{ \"prompt\": \"a fox\" }"
        /// </summary>
        public static async Task InsertFunctionCallAsync(FoxUser user, string functionName, string parametersJson, long? finalId)
        {
            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_function_calls (user_id, function_name, parameters, final_id, created_at)
                VALUES (@uid, @fn, @pj, @fid, @now)
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@fn", functionName);
            cmd.Parameters.AddWithValue("@pj", parametersJson);
            cmd.Parameters.AddWithValue("@fid", finalId);
            cmd.Parameters.AddWithValue("@now", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
