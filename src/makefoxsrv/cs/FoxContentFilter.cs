using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MySqlConnector;

using makefoxsrv;
using TL;
using static System.Net.Mime.MediaTypeNames;
using WTelegram;
using System.Collections;

namespace makefoxsrv
{
    public static class FoxContentFilter
    {
        public static bool ImageTagsSafetyCheck(Dictionary<string, float> imageTags)
        {
            if (imageTags is null || imageTags.Count == 0)
                return true; // No tags, assume safe.

            // Check for explicit content tags with high probability.

            if (imageTags.ContainsKey("human") && !imageTags.ContainsKey("anthro") && (imageTags.ContainsKey("child") || imageTags.ContainsKey("young")))
            {
                if (imageTags.ContainsKey("penis") || imageTags.ContainsKey("pussy"))
                    return false;

                if (imageTags.ContainsKey("nude") && (imageTags.ContainsKey("shota") || imageTags.ContainsKey("loli")))
                    return false;
            }

            return true;
        }


        public enum EmbeddingType
        {
            USER_PROMPT,
            PREDICTED_TAGS
        }

        public enum SafetyStatus
        {
            NONE,
            UNSAFE,
            SAFE,
            FALSE_POSITIVE
        }


        public static async Task<double> GetSafetyScoreAsync(
            ulong qid,
            EmbeddingType embeddingType,
            SafetyStatus targetStatus,
            int limit = 1000)
        {
            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            // Fetch embedding for this qid
            string? embeddingText = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT VEC_ToText(embedding) as embedding
                    FROM queue_embeddings
                    WHERE qid = @qid AND type = @type
                    LIMIT 1;";
                cmd.Parameters.AddWithValue("@qid", qid);
                cmd.Parameters.AddWithValue("@type", embeddingType == EmbeddingType.USER_PROMPT ? "USER_PROMPT" : "PREDICTED_TAGS");

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    return 0.0;
                embeddingText = result.ToString();
            }

            // Map enum to DB value
            string status = targetStatus switch
            {
                SafetyStatus.UNSAFE => "UNSAFE",
                SafetyStatus.SAFE => "SAFE",
                SafetyStatus.FALSE_POSITIVE => "FALSE_POSITIVE",
                _ => "NONE"
            };

            // Compare with rows of that status
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT AVG(1 - VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec))) AS avg_similarity
                    FROM (
                        SELECT embedding
                        FROM queue_embeddings
                        WHERE type = @type
                          AND safety_status = @status
                          AND qid <> @qid
                        ORDER BY VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec)) ASC
                        LIMIT @limit
                    ) sub;";
                cmd.Parameters.AddWithValue("@vec", embeddingText!);
                cmd.Parameters.AddWithValue("@type", embeddingType == EmbeddingType.USER_PROMPT ? "USER_PROMPT" : "PREDICTED_TAGS");
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@qid", qid);
                cmd.Parameters.AddWithValue("@limit", limit);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDouble(result);
            }

            return 0.0;
        }



        private static List<ContentFilterRule> _rules = new List<ContentFilterRule>();

        /// <summary>
        /// Represents a content filter rule with prompt and negative_prompt patterns
        /// </summary>
        public class ContentFilterRule
        {
            public ulong Id { get; set; }
            public string Prompt { get; set; } = string.Empty;
            public string? NegativePrompt { get; set; }
            public string? Description { get; set; }
            public bool Enabled { get; set; } = true;

            // Compiled regex for efficient matching
            public Regex? CompiledPromptRegex { get; set; }
            public Regex? CompiledNegativePromptRegex { get; set; }
        }

        /// <summary>
        /// Loads all filter rules from the database and compiles them into regexes
        /// </summary>
        public static async Task LoadRulesAsync()
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(
                    "SELECT id, prompt, negative_prompt, description, enabled " +
                    "FROM content_filter_rules WHERE enabled = 1", connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        _rules.Clear();

                        while (await reader.ReadAsync())
                        {
                            var rule = new ContentFilterRule
                            {
                                Id = Convert.ToUInt64(reader["id"]),
                                Prompt = reader["prompt"].ToString()!,
                                NegativePrompt = reader["negative_prompt"] as string,
                                Description = reader["description"] as string,
                                Enabled = Convert.ToBoolean(reader["enabled"])
                            };

                            // Compile regex patterns for the rule
                            CompileRuleRegex(rule);

                            _rules.Add(rule);
                        }
                    }
                }
            }

            Console.WriteLine($"Loaded and compiled {_rules.Count} content filter rules.");
        }

        /// <summary>
        /// Compiles a rule's patterns into optimized regexes
        /// </summary>
        private static void CompileRuleRegex(ContentFilterRule rule)
        {
            if (!string.IsNullOrEmpty(rule.Prompt))
            {
                string regexPattern = ConvertExpressionToRegex(rule.Prompt);
                Console.WriteLine($"Compiled prompt pattern for rule {rule.Id}: {regexPattern}");
                rule.CompiledPromptRegex = new Regex(regexPattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            if (!string.IsNullOrEmpty(rule.NegativePrompt))
            {
                string regexPattern = ConvertExpressionToRegex(rule.NegativePrompt);
                Console.WriteLine($"Compiled negative pattern for rule {rule.Id}: {regexPattern}");
                rule.CompiledNegativePromptRegex = new Regex(regexPattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
        }

        /// <summary>
        /// Converts a logical expression to an equivalent regex pattern
        /// </summary>
        private static string ConvertExpressionToRegex(string expression)
        {
            // Normalize the expression before parsing
            expression = expression.Trim();
            Console.WriteLine($"Converting expression to regex: {expression}");

            try
            {
                // First, check for top-level OR operations
                List<string> orParts = SplitByTopLevelOperator(expression, '|');

                // If we have multiple OR parts at the top level
                if (orParts.Count > 1)
                {
                    Console.WriteLine($"Found top-level OR expression with {orParts.Count} parts: {string.Join(" | ", orParts)}");

                    // Process each OR part and combine with OR semantics
                    StringBuilder orBuilder = new StringBuilder();
                    orBuilder.Append("(?:");

                    for (int i = 0; i < orParts.Count; i++)
                    {
                        // Process each OR branch (which might have AND operations)
                        string orPartRegex = ConvertExpressionAndParts(orParts[i].Trim());

                        if (i > 0)
                        {
                            orBuilder.Append("|");
                        }

                        orBuilder.Append(orPartRegex);
                    }

                    orBuilder.Append(")");

                    string regexPattern = orBuilder.ToString();
                    Console.WriteLine($"Final regex pattern (OR): {regexPattern}");
                    return regexPattern;
                }
                else
                {
                    // No top-level OR, process as AND parts
                    return ConvertExpressionAndParts(expression);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compiling rule expression: {expression}. Error: {ex.Message}");
                // Return a regex that will never match if compilation fails
                return "(?!.*)";
            }
        }

        /// <summary>
        /// Processes a logical expression with AND parts
        /// </summary>
        private static string ConvertExpressionAndParts(string expression)
        {
            // Split by AND operators at the top level (outside parentheses)
            List<string> andParts = SplitByTopLevelOperator(expression, '&');
            StringBuilder regexBuilder = new StringBuilder();

            Console.WriteLine($"Split into {andParts.Count} AND parts: {string.Join(", ", andParts)}");

            // Process each part separately and combine with AND semantics (all must match)
            foreach (string part in andParts)
            {
                // Process individual AND part (which might have OR operations inside)
                string trimmedPart = part.Trim();
                string partRegex = ProcessExpression(trimmedPart);
                regexBuilder.Append(partRegex);
                Console.WriteLine($"Added AND part: {trimmedPart} -> {partRegex}");
            }

            string regexPattern = regexBuilder.ToString();
            Console.WriteLine($"Combined AND parts: {regexPattern}");
            return regexPattern;
        }

        /// <summary>
        /// Splits an expression by a top-level operator (outside of parentheses)
        /// </summary>
        private static List<string> SplitByTopLevelOperator(string expression, char op)
        {
            List<string> parts = new List<string>();
            int start = 0;
            int parenLevel = 0;

            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];

                if (c == '(')
                {
                    parenLevel++;
                }
                else if (c == ')')
                {
                    parenLevel--;
                }
                else if (c == op && parenLevel == 0)
                {
                    // Found top-level operator
                    parts.Add(expression.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            // Add the last part
            if (start < expression.Length)
            {
                parts.Add(expression.Substring(start).Trim());
            }

            return parts;
        }

        /// <summary>
        /// Processes a single expression part (possibly containing OR operations)
        /// </summary>
        /// <summary>
        /// Processes a single expression part (possibly containing OR operations)
        /// </summary>
        private static string ProcessExpression(string expression)
        {
            Console.WriteLine($"Processing expression part: {expression}");

            // Strip outer parentheses if present
            if (expression.StartsWith("(") && expression.EndsWith(")"))
            {
                string inner = expression.Substring(1, expression.Length - 2).Trim();

                // Check if we have OR operations inside
                if (inner.Contains("|"))
                {
                    // Split by '|' at top level
                    List<string> orParts = SplitByTopLevelOperator(inner, '|');
                    Console.WriteLine($"Split into {orParts.Count} OR parts: {string.Join(", ", orParts)}");

                    List<string> positiveTerms = new List<string>();
                    List<string> negativeTerms = new List<string>();

                    foreach (string orPart in orParts)
                    {
                        string trimmed = orPart.Trim();
                        if (trimmed.StartsWith("!"))
                        {
                            // Collect negative tokens (tokens that must NOT appear)
                            string term = trimmed.Substring(1).Trim();
                            negativeTerms.Add(term);
                            Console.WriteLine($"Collected negated term: {term}");
                        }
                        else
                        {
                            // Collect positive tokens (tokens that must appear)
                            positiveTerms.Add(trimmed);
                            Console.WriteLine($"Collected positive term: {trimmed}");
                        }
                    }

                    // Instead of alternating (OR), require positives and forbidden negatives concurrently
                    string result = "";
                    if (positiveTerms.Count > 0)
                    {
                        string alternation = string.Join("|", positiveTerms.Select(term => Regex.Escape(term)));
                        result += $"(?=.*\\b(?:{alternation})\\b)";
                    }
                    if (negativeTerms.Count > 0)
                    {
                        result += string.Concat(negativeTerms.Select(term => $"(?!.*\\b{Regex.Escape(term)}\\b)"));
                    }
                    return result;
                }
                else if (inner.StartsWith("!"))
                {
                    // Handle simple negation within parentheses
                    string term = inner.Substring(1).Trim();
                    string negatedRegex = $"(?!.*\\b{Regex.Escape(term)}\\b)";
                    Console.WriteLine($"Processed negation: {inner} -> {negatedRegex}");
                    return negatedRegex;
                }
                else
                {
                    // Handle simple positive term within parentheses
                    string positiveRegex = $"(?=.*\\b{Regex.Escape(inner)}\\b)";
                    Console.WriteLine($"Processed parenthesized term: {inner} -> {positiveRegex}");
                    return positiveRegex;
                }
            }
            else if (expression.StartsWith("!"))
            {
                // Handle simple negation outside of parentheses
                string term = expression.Substring(1).Trim();
                string negatedRegex = $"(?!.*\\b{Regex.Escape(term)}\\b)";
                Console.WriteLine($"Processed negation: {expression} -> {negatedRegex}");
                return negatedRegex;
            }
            else
            {
                // Handle simple term outside of parentheses
                string positiveRegex = $"(?=.*\\b{Regex.Escape(expression)}\\b)";
                Console.WriteLine($"Processed term: {expression} -> {positiveRegex}");
                return positiveRegex;
            }
        }



        /// <summary>
        /// Checks if a prompt violates any content filter rules
        /// </summary>
        /// <param name="positivePrompt">The positive prompt to check</param>
        /// <param name="negativePrompt">The negative prompt to check (optional)</param>
        /// <returns>List of rules that were violated, or empty list if none</returns>
        public static List<ContentFilterRule> CheckPrompt(string positivePrompt, string? negativePrompt = null)
        {
            var violatedRules = new List<ContentFilterRule>();

            try
            {
                // Normalize inputs by removing character substitutions
                positivePrompt = NormalizeForMatching(positivePrompt);
                negativePrompt = negativePrompt != null
                    ? NormalizeForMatching(negativePrompt)
                    : string.Empty;

                foreach (var rule in _rules)
                {
                    try
                    {
                        bool positiveMatch = rule.CompiledPromptRegex?.IsMatch(positivePrompt) ?? false;
                        bool negativeMatch = true; // Default to true if no negative pattern

                        // If rule has a negative pattern, evaluate it
                        if (rule.CompiledNegativePromptRegex != null)
                        {
                            negativeMatch = rule.CompiledNegativePromptRegex.IsMatch(negativePrompt);
                        }

                        // Rule matches if both positive and negative patterns match
                        if (positiveMatch && negativeMatch)
                            violatedRules.Add(rule);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't crash
                        FoxLog.WriteLine($"Error evaluating rule {rule.Id}: {ex.Message}", LogLevel.ERROR);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't crash
                FoxLog.WriteLine($"Error in CheckPrompt: {ex.Message}", LogLevel.ERROR);
            }

            return violatedRules;
        }

        /// <summary>
        /// Normalizes text for matching - handles common evasion techniques
        /// </summary>
        private static string NormalizeForMatching(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // Replace common character substitutions before regex matching
            string result = input.ToLower();

            // Replace numbers/symbols commonly used to evade filters
            result = result.Replace("0", "o");
            result = result.Replace("1", "i");
            result = result.Replace("3", "e");
            result = result.Replace("4", "a");
            result = result.Replace("5", "s");
            result = result.Replace("@", "a");

            return result;
        }

        /// <summary>
        /// Records multiple rule violations in the database
        /// </summary>
        /// <param name="queueId">The queue ID of the processed image request</param>
        /// <param name="ruleIds">The list of violated rule IDs</param>
        public static async Task RecordViolationsAsync(ulong queueId, List<ulong> ruleIds)
        {
            if (ruleIds == null || ruleIds.Count == 0)
                return;

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var sb = new StringBuilder();
                sb.Append("INSERT INTO content_filter_violations (queue_id, rule_id) VALUES ");

                using (var command = new MySqlCommand())
                {
                    command.Connection = connection;

                    for (int i = 0; i < ruleIds.Count; i++)
                    {
                        string queueParam = $"@queueId{i}";
                        string ruleParam = $"@ruleId{i}";

                        sb.Append($"({queueParam}, {ruleParam})");

                        if (i < ruleIds.Count - 1)
                            sb.Append(", ");

                        command.Parameters.Add(queueParam, MySqlDbType.UInt64).Value = queueId;
                        command.Parameters.Add(ruleParam, MySqlDbType.UInt64).Value = ruleIds[i];
                    }

                    command.CommandText = sb.ToString();
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public static async Task<(double avgFalsePos, double avgUnsafe)> GetPromptSafetyScoresAsync(long qid, int limit = 1000)
        {
            double avgFalsePos = 0.0;
            double avgUnsafe = 0.0;

            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            // Fetch this qid's user prompt embedding
            string? embeddingText = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT embedding
                    FROM queue_embeddings
                    WHERE qid = @qid AND type = 'USER_PROMPT'
                    LIMIT 1;";
                cmd.Parameters.AddWithValue("@qid", qid);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    return (0.0, 0.0); // no embedding stored
                embeddingText = result.ToString();
            }

            // Compute average similarity vs UNSAFE prompts
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT AVG(1 - VEC_DISTANCE_COSINE(embedding, @vec)) AS avg_similarity
                    FROM (
                        SELECT embedding
                        FROM queue_embeddings
                        WHERE type = 'USER_PROMPT'
                          AND safety_status = 'UNSAFE'
                        ORDER BY VEC_DISTANCE_COSINE(embedding, @vec) ASC
                        LIMIT @limit
                    ) sub;";
                cmd.Parameters.AddWithValue("@vec", embeddingText!);
                cmd.Parameters.AddWithValue("@limit", limit);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    avgUnsafe = Convert.ToDouble(result);
            }

            // Compute average similarity vs FALSE_POSITIVE prompts
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT AVG(1 - VEC_DISTANCE_COSINE(embedding, @vec)) AS avg_similarity
                    FROM (
                        SELECT embedding
                        FROM queue_embeddings
                        WHERE type = 'USER_PROMPT'
                          AND safety_status = 'FALSE_POSITIVE'
                        ORDER BY VEC_DISTANCE_COSINE(embedding, @vec) ASC
                        LIMIT @limit
                    ) sub;";
                cmd.Parameters.AddWithValue("@vec", embeddingText!);
                cmd.Parameters.AddWithValue("@limit", limit);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    avgFalsePos = Convert.ToDouble(result);
            }

            return (avgFalsePos, avgUnsafe);
        }

        public record ViolationRecord(ulong QueueId, ulong RuleId, ulong Uid);

        [Cron(seconds: 20)]
        public static async Task CronNotifyPendingViolations()
        {
            List<ViolationRecord> violations = new List<ViolationRecord>();

            if (!FoxTelegram.IsConnected)
                throw new Exception("Telegram is not connected");

            var moderationGroupId = FoxSettings.Get<long>("ModerationGroupID");

            if (moderationGroupId == 0)
                throw new Exception("Moderation group ID is not set or invalid");

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                // Retrieve violations with acknowledged IS NULL and queue status not PAUSED/CANCELLED.
                string sql = @"
                    SELECT q.uid, cf.queue_id, cf.rule_id
                    FROM content_filter_violations cf
                    JOIN queue q ON cf.queue_id = q.id
                    WHERE cf.acknowledged IS NULL
                    AND q.status NOT IN ('PAUSED')";

                using (var selectCmd = new MySqlCommand(sql, connection))
                {
                    using (var reader = await selectCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            ulong queueId = Convert.ToUInt64(reader["queue_id"]);
                            ulong ruleId = Convert.ToUInt64(reader["rule_id"]);
                            ulong uid = Convert.ToUInt64(reader["uid"]);

                            violations.Add(new ViolationRecord(queueId, ruleId, uid));
                        }
                    }
                }

                if (violations.Count == 0)
                {
                    Console.WriteLine("No pending violations found.");
                    return;
                }

                // Group violations by uid and send notifications to moderation group.
                var grouped = violations.GroupBy(v => v.Uid);

                foreach (var group in grouped)
                {

                    ulong uid = group.Key;                            // the user id
                    int count = group.Count();                        // how many violations
                    var rules = group.Select(v => v.RuleId).ToList(); // which rules they broke

                    var message = $"User {uid} has {count} violations.";

                    List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonUrl[]
                        {
                            new() { text = "Image Viewer", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={uid}"}
                        }
                    });

                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonUrl[]
                        {
                            new() { text = "View Violations", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={uid}&violations=1&vioall=1"}
                        }
                    });

                    var queueIds = group.Select(v => v.QueueId).Distinct();

                    foreach (var queueId in queueIds)
                    {

                        

                        var userFalsePosScore = await GetSafetyScoreAsync(queueId, EmbeddingType.USER_PROMPT, SafetyStatus.FALSE_POSITIVE);
                        var userUnsafeScore = await GetSafetyScoreAsync(queueId, EmbeddingType.USER_PROMPT, SafetyStatus.UNSAFE);

                        var tagsFalsePosScore = await GetSafetyScoreAsync(queueId, EmbeddingType.PREDICTED_TAGS, SafetyStatus.FALSE_POSITIVE);
                        var tagsUnsafeScore = await GetSafetyScoreAsync(queueId, EmbeddingType.PREDICTED_TAGS, SafetyStatus.UNSAFE);

                        var btnText = $"🖼️ ({userFalsePosScore:F2}, {userUnsafeScore:F2}) ({tagsFalsePosScore:F2}, {tagsUnsafeScore:F2})";


                        buttonRows.Add(new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonUrl[]
                            {
                                new() { text = btnText, url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?id={queueId}&violations=1&vioall=1" }
                            }
                        });
                    }

                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "🙃", data = System.Text.Encoding.ASCII.GetBytes("/admin_mod falsepos " + String.Join(",", queueIds)) },
                                new TL.KeyboardButtonCallback { text = "☠️", data = System.Text.Encoding.ASCII.GetBytes("/admin_mod unsafe " + String.Join(",", queueIds)) },
                            }
                    });

                    try
                    {
                        await SendModerationNotification(message, new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() });

                        // Acknowledge each queue_id once
                        foreach (var queueId in group.Select(v => v.QueueId).Distinct())
                        {
                            using (var updateCmd = new MySqlCommand(
                                "UPDATE content_filter_violations " +
                                "SET acknowledged = @ackTime " +
                                "WHERE queue_id = @queueId", connection))
                            {
                                updateCmd.Parameters.Add("@ackTime", MySqlDbType.DateTime).Value = DateTime.Now;
                                updateCmd.Parameters.Add("@queueId", MySqlDbType.UInt64).Value = queueId;

                                await updateCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex, $"Error processing violations: {ex.Message}");
                    }
                }
            }
        }

        // Stub method: implement your actual message-sending logic here.
        public static async Task SendModerationNotification(string messageText, TL.ReplyInlineMarkup? replyKeyboardMarkup = null)
        {
            var moderationGroupId = FoxSettings.Get<long>("ModerationGroupID");

            if (moderationGroupId == 0)
                return;

            // For example, send this message via email, a messaging API, or log it as needed.
            FoxLog.WriteLine($"Sending moderation notification:\r\n{messageText}");

            var moderationGroup = await FoxTelegram.GetChatFromID(moderationGroupId);

            if (moderationGroup is null)
                throw new Exception("Moderation group not found");

            InputReplyToMessage? inputReplyToMessage = null;

            var moderationTopicId = FoxSettings.Get<int?>("ModerationGroupTopicID");

            if (moderationTopicId is not null)
                inputReplyToMessage = new InputReplyToMessage { reply_to_msg_id = moderationTopicId.Value, top_msg_id = moderationTopicId.Value };

            await FoxTelegram.Client.Messages_SendMessage(
                peer: moderationGroup,
                random_id: Helpers.RandomLong(),
                message: messageText,
                reply_to: inputReplyToMessage,
                reply_markup: replyKeyboardMarkup
            );
        }
    }
}