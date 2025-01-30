﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tiktoken; // Your Tiktoken library namespace

namespace makefoxsrv
{
    /// <summary>
    /// Represents a single chat message (role + content), similar to ChatGPT's usage:
    /// { "role": "system|user|assistant", "content": "some text" }
    /// </summary>
    public record ChatMessage(string role, string content);

    public static class LLMConversationBuilder
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
        public static async Task<List<ChatMessage>> BuildConversationAsync(
            FoxUser user,
            string latestUserRequest,
            int maxTokens)
        {
            var messages = new List<ChatMessage>();

            // 1) Divide token budgets: 70% for memory, 30% for conversation logs
            int memoryBudget = (int)(maxTokens * 0.70);
            int convoBudget = maxTokens - memoryBudget;

            // 2) Build the memory prompt first, so we know how many tokens it might consume
            var encoder = ModelToEncoder.For("gpt-4o"); // or your chosen model/encoding
            var memoryPrompt = await BuildMemoryPromptAsync(user, memoryBudget, encoder);
            int memoryPromptTokens = (memoryPrompt == null) ? 0 : encoder.CountTokens(memoryPrompt.content);

            // Reallocate leftover from memory to conversation if any
            int memoryUsed = memoryPromptTokens;
            int memoryLeftover = memoryBudget - memoryUsed;
            convoBudget += Math.Max(0, memoryLeftover);

            // 3) Fetch conversation + function calls in descending order,
            //    parse & accumulate them until we run out of convoBudget tokens.
            var reversedMessages = await FetchMessages(user, convoBudget, encoder);

            // 4) Insert timestamps every 10 minutes, including before the first message.
            //    We'll build a new list for the final conversation with timestamps.
            var finalConversation = new List<ChatMessage>();
            DateTime? lastTs = null;

            foreach (var msg in reversedMessages)
            {
                // If the user wants a timestamp before the first message, we check:
                // If lastTs is null, we do one immediately. 
                // Then each time >=10 min passes, we do another.
                bool needTimestamp;
                if (!lastTs.HasValue)
                {
                    // They asked for a timestamp "including before the first message."
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
                    int tsTokens = encoder.CountTokens(timeLabel);
                    if (tsTokens <= convoBudget)
                    {
                        finalConversation.Add(new ChatMessage("system", timeLabel));
                        convoBudget -= tsTokens;
                    }
                    else
                    {
                        FoxLog.WriteLine($"[Warning] No space for timestamp, forcibly adding (over by {tsTokens - convoBudget} tokens).");
                        finalConversation.Add(new ChatMessage("system", timeLabel));
                        convoBudget = 0;
                    }

                    lastTs = msg.Item2;
                }

                // Now add the actual message
                int msgTokens = encoder.CountTokens(msg.Item1.content);
                if (msgTokens <= convoBudget)
                {
                    finalConversation.Add(msg.Item1);
                    convoBudget -= msgTokens;
                }
                else
                {
                    FoxLog.WriteLine($"[Warning] No space for message, forcibly adding (over by {msgTokens - convoBudget} tokens).");
                    finalConversation.Add(msg.Item1);
                    convoBudget = 0;
                }
            }

            // 5) Insert one more timestamp right before the memory prompt & user request, if 10 min passed
            var now = DateTime.Now;

            var finalTs = $"Timestamp: {now:HH:mm, yyyy-MM-dd}";
            convoBudget -= encoder.CountTokens(finalTs);
            finalConversation.Add(new ChatMessage("system", finalTs));

            // 6) Insert memory prompt now (if it exists)
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

            // 7) Finally, the user's newest request
            int userReqTokens = encoder.CountTokens(latestUserRequest);
            if (userReqTokens <= (maxTokens - userReqTokens))
            {
                finalConversation.Add(new ChatMessage("user", latestUserRequest));
            }
            else
            {
                FoxLog.WriteLine($"[Warning] forcibly adding latest user request (over by {userReqTokens - (maxTokens - userReqTokens)} tokens).");
                finalConversation.Add(new ChatMessage("user", latestUserRequest));
            }

            // Insert it into the database AFTER everything else, so that it doesn't end up going in twice.
            await InsertConversationMessageAsync(user, "user", latestUserRequest);

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
                  c.created_at   AS created_at
                FROM llm_conversations c
                WHERE c.user_id = @uid

                UNION ALL

                SELECT
                  'func'         AS row_type,
                  'system'       AS role,
                  ''             AS content,
                  f.function_name AS function_name,
                  f.parameters   AS raw_params,
                  f.created_at   AS created_at
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
                $"You (the AI) called the function {functionName} with the parameters:"
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
        public static async Task InsertConversationMessageAsync(FoxUser user, string role, string content)
        {
            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_conversations (user_id, role, content, created_at)
                VALUES (@uid, @role, @ct, @now)
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@role", role);
            cmd.Parameters.AddWithValue("@ct", content);
            cmd.Parameters.AddWithValue("@now", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Inserts a function call into llm_function_calls, storing JSON parameters.
        /// e.g., functionName = "GenerateImage", parametersJson = "{ \"prompt\": \"a fox\" }"
        /// </summary>
        public static async Task InsertFunctionCallAsync(FoxUser user, string functionName, string parametersJson)
        {
            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO llm_function_calls (user_id, function_name, parameters, created_at)
                VALUES (@uid, @fn, @pj, @now)
            ", conn);

            cmd.Parameters.AddWithValue("@uid", user.UID);
            cmd.Parameters.AddWithValue("@fn", functionName);
            cmd.Parameters.AddWithValue("@pj", parametersJson);
            cmd.Parameters.AddWithValue("@now", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
