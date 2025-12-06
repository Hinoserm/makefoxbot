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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using SixLabors.Fonts.Tables.AdvancedTypographic;

namespace makefoxsrv
{
    public static class FoxContentFilter
    {
        public static async Task<ModerationResult> CheckUserIntentAsync(FoxQueue q)
        {
            if (string.IsNullOrEmpty(FoxMain.settings?.llmApiKey))
                throw new InvalidOperationException("LLM API key not configured.");

            var model = "x-ai/grok-4-fast";

            var sysPrompt = new StringBuilder();

            sysPrompt.AppendLine("You are a moderation assistant for an AI image generation service.");
            sysPrompt.AppendLine("Your task is to determine whether a user's prompt or generated image indicates an intent to create disallowed content.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("POLICY OVERVIEW:");
            sysPrompt.AppendLine("- Sexual content involving underage humans or very human-like fantasy species (elves, dwarves) is strictly prohibited.");
            sysPrompt.AppendLine("- \"Cub\" furry content (underage anthropomorphic animals) is permitted, even in sexually explicit or extreme contexts.");
            sysPrompt.AppendLine("- When a young anthropomorphic character is paired with an adult human, it is allowed only if the anthro character is clearly animal-like and not human-passing.");
            sysPrompt.AppendLine("- Sexual content involving adult humans or adult anthropomorphic characters is allowed.");
            sysPrompt.AppendLine("- Content involving non-human entities (monsters, aliens, robots, etc.) is allowed.");
            sysPrompt.AppendLine("- Extreme themes such as rape, torture, or brutality are allowed.");
            sysPrompt.AppendLine("- IMPORTANT: Anthropomorphic \"furry\" child characters ARE FINE; it must be VISIBLY HUMAN to be considered a violation.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("EVALUATION GUIDELINES:");
            sysPrompt.AppendLine("- Consider the entire context of the user’s prompts, negative prompts, and predicted tags.");
            sysPrompt.AppendLine("- If an uninformed observer would clearly interpret the content as a human child in a sexual context, treat it as a violation.");
            sysPrompt.AppendLine("- Judge based primarily on user intent, not aesthetic similarity or accidental phrasing.");
            sysPrompt.AppendLine("- If intent is ambiguous or appears accidental, return intent = 'accidental' with low confidence.");
            sysPrompt.AppendLine("- If the user appears deliberately attempting to bypass or provoke the rules, return intent = 'deliberate' with higher confidence.");
            sysPrompt.AppendLine("- Our users often like to generate non-human children engaging in acts with adult human characters; be precise when judging these, as this content IS ALLOWED.");
            //sysPrompt.AppendLine("- Predicted tags are unreliable; focus on looking at the actual image.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("INPUT INFORMATION:");
            sysPrompt.AppendLine("- You will be provided the user's prompts, negative prompts, and predicted tags from the vision model.");
            sysPrompt.AppendLine("- In some cases, you may also be shown the generated image(s) for additional context.");
            sysPrompt.AppendLine("- Always treat the user prompts as the primary evidence of user intent.");
            sysPrompt.AppendLine("- If the predicted tags or image indicate that an underage human may be depicted, this is still a violation, even if the user didn't intend it.  In these cases, set intent=accidental and violation=true.  Explain the issue in the user_message.");
            sysPrompt.AppendLine("- Remember, the image MUST include UNDERAGE HUMANS to quality as a violation of any kind.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("EXAMPLES OF FULLY ACCEPTABLE CONTENT:");
            sysPrompt.AppendLine("- furry toddler sucking the dick of an adult male human");
            sysPrompt.AppendLine("- loli, young, fox, female, nude, pussy");
            sysPrompt.AppendLine("- foxes, dogs, goats, cats, and any other animal, feral or anthro");
            sysPrompt.AppendLine("- underage anthropomorphic characters in a potentially intimate or nude context, including visible genitals");
            sysPrompt.AppendLine("- an adult human in an explicit scene with an underage non-human");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("EXAMPLES OF UNACCEPTABLE CONTENT:");
            sysPrompt.AppendLine("- Sexualized underage elves, goblins, or humans");
            sysPrompt.AppendLine("- Human-like characters with extremely minimal animal features, such as creatures that look almost completely human except for having animal ears or tail.");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("USER MESSAGE:");
            sysPrompt.AppendLine("- If the content violates policy, include a short, user-facing message (user_message) explaining how the content violates our policy.");
            sysPrompt.AppendLine("- If no violation occurred, leave user_message empty or null.");
            sysPrompt.AppendLine("- You must NEVER tell the user that anthropomorphic content is prohibited, because it is not.");
            sysPrompt.AppendLine("- If you think the violation was accidential, explain this to the user.");
            sysPrompt.AppendLine("ADMIN MESSAGE:");
            sysPrompt.AppendLine("- Use admin_message to include a detailed explaination of your decision for auditing purposes.");

            // Fetch image tags
            var outputImage = await q.GetOutputImage();
            //var predictedTags = await outputImage.GetImageTagsAsync();

            var inputPayload = new
            {
                prompt = q.Settings.Prompt,
                negative_prompt = q.Settings.NegativePrompt ?? "",
                //predicted_tags = predictedTags
            };

            var jpegImage = outputImage.GetImageAsJpeg(60, 768);
            string base64ImageStr = Convert.ToBase64String(jpegImage, Base64FormattingOptions.None);
            string dataUrl = $"data:image/jpeg;base64,{base64ImageStr}";

            var messageList = new object[]
            {
                new
                {
                    role = "system",
                    content = sysPrompt.ToString()
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = JsonConvert.SerializeObject(inputPayload) },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            };

            var requestBody = new
            {
                model,
                user = $"MDR8R:{q.User.UID}:{q.ID}",
                reasoning = new
                {
                    enabled = true
                },
                messages = messageList,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "moderation_result",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                { "violation",  new { type = "boolean", description = "True if the user's content violates policy" } },
                                { "intent",     new { type = "string",  description = "User's apparent intent behind the violation", enum_values = new[] { "none", "accidental", "deliberate" } } },
                                { "confidence", new { type = "integer", description = "Confidence from 0–10 about the intent judgment" } },
                                { "user_message", new { type = "string", description = "A short, polite message explaining to the user why their image was blocked or flagged. Keep under 200 characters." } },
                                { "admin_message", new { type = "string", description = "Explain your decision for record keeping and debugging purposes. Keep under 600 characters." } }
                            },
                            required = new[] { "violation", "intent", "confidence" },
                            additionalProperties = false
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            using var http = new HttpClient();

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FoxMain.settings.llmApiKey);

            var apiUrl = "https://openrouter.ai/api/v1/chat/completions";

            // Fix: use proper endpoint, not API key
            var response = await http.PostAsync(
                apiUrl,
                new StringContent(json, Encoding.UTF8, "application/json"));

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"OpenRouter request failed: {response.StatusCode}\n{content}");

            var parsed = JObject.Parse(content);
            var rawJson = parsed["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new Exception("Empty or invalid LLM response.");

            var result = JsonConvert.DeserializeObject<ModerationResult>(rawJson)
                         ?? throw new Exception("Failed to deserialize structured moderation result.");

            // Clamp the confidence for sanity
            result.confidence = Math.Clamp(result.confidence, 0, 10);

            // Insert into safety_llm_responses
            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO safety_llm_responses
                    ( queue_id, violation, intent, confidence, user_message, admin_message, recorded_at )
                    VALUES
                    ( @queue_id, @violation, @intent, @confidence, @user_message, @admin_message, @recorded_at );";
                cmd.Parameters.AddWithValue("@queue_id", q.ID);
                cmd.Parameters.AddWithValue("@violation", result.violation ? 1 : 0);
                cmd.Parameters.AddWithValue("@intent", result.intent.ToString().ToUpper());
                cmd.Parameters.AddWithValue("@confidence", result.confidence);
                cmd.Parameters.AddWithValue("@user_message", result.user_message);
                cmd.Parameters.AddWithValue("@admin_message", result.admin_message);
                cmd.Parameters.AddWithValue("@recorded_at", DateTime.Now);

                await cmd.ExecuteNonQueryAsync();
            }

            return result;
        }

        public class ModerationResult
        {
            public bool violation { get; set; }

            [JsonConverter(typeof(StringEnumConverter))]
            public ModerationIntent intent { get; set; } = ModerationIntent.None;

            public int confidence { get; set; }

            public string? user_message { get; set; }

            public string? admin_message { get; set; }

        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum ModerationIntent
        {
            [EnumMember(Value = "none")]
            None,

            [EnumMember(Value = "accidental")]
            Accidental,

            [EnumMember(Value = "deliberate")]
            Deliberate
        }

        public static bool ImageTagsSafetyCheck(Dictionary<string, float> imageTags)
        {
            if (imageTags is null || imageTags.Count == 0)
                return true; // No tags, assume safe.

            // Check for explicit content tags with high probability.

            bool isHumanish = imageTags.ContainsKey("human")
                           || imageTags.ContainsKey("elf")
                           || imageTags.ContainsKey("dwarf")
                           //|| imageTags.ContainsKey("kemono") // WAY too many false positives
                           || imageTags.ContainsKey("neko");
                           //|| imageTags.ContainsKey("goblin");

            bool isYoung = imageTags.ContainsKey("child") 
                        || imageTags.ContainsKey("young") 
                        || imageTags.ContainsKey("teen")  
                        || imageTags.ContainsKey("shota") 
                        || imageTags.ContainsKey("loli");

            bool isExplicit = imageTags.ContainsKey("penis")
                           || imageTags.ContainsKey("pussy")
                           || imageTags.ContainsKey("vagina")
                           || imageTags.ContainsKey("nude")
                           || imageTags.ContainsKey("sex")
                           || imageTags.ContainsKey("foreskin")
                           || imageTags.ContainsKey("genitals")
                           || imageTags.ContainsKey("erection")
                           || imageTags.ContainsKey("glans")
                              // Added female-specific tags
                           || imageTags.ContainsKey("vulva")
                           || imageTags.ContainsKey("clitoris")
                           || imageTags.ContainsKey("labia")
                           || imageTags.ContainsKey("breasts")
                           || imageTags.ContainsKey("nipples")
                           || imageTags.ContainsKey("areola");

            // If human-like and young with explicit content, block.

            if (isHumanish && isYoung && isExplicit)
                return false; // Not safe

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
                //Console.WriteLine($"Compiled negative pattern for rule {rule.Id}: {regexPattern}");
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
            //Console.WriteLine($"Converting expression to regex: {expression}");

            try
            {
                // First, check for top-level OR operations
                List<string> orParts = SplitByTopLevelOperator(expression, '|');

                // If we have multiple OR parts at the top level
                if (orParts.Count > 1)
                {
                    //Console.WriteLine($"Found top-level OR expression with {orParts.Count} parts: {string.Join(" | ", orParts)}");

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
                    //Console.WriteLine($"Final regex pattern (OR): {regexPattern}");
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
                //Console.WriteLine($"Error compiling rule expression: {expression}. Error: {ex.Message}");
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

            //Console.WriteLine($"Split into {andParts.Count} AND parts: {string.Join(", ", andParts)}");

            // Process each part separately and combine with AND semantics (all must match)
            foreach (string part in andParts)
            {
                // Process individual AND part (which might have OR operations inside)
                string trimmedPart = part.Trim();
                string partRegex = ProcessExpression(trimmedPart);
                regexBuilder.Append(partRegex);
                //Console.WriteLine($"Added AND part: {trimmedPart} -> {partRegex}");
            }

            string regexPattern = regexBuilder.ToString();
            //Console.WriteLine($"Combined AND parts: {regexPattern}");
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
            //Console.WriteLine($"Processing expression part: {expression}");

            // Strip outer parentheses if present
            if (expression.StartsWith("(") && expression.EndsWith(")"))
            {
                string inner = expression.Substring(1, expression.Length - 2).Trim();

                // Check if we have OR operations inside
                if (inner.Contains("|"))
                {
                    // Split by '|' at top level
                    List<string> orParts = SplitByTopLevelOperator(inner, '|');
                    //Console.WriteLine($"Split into {orParts.Count} OR parts: {string.Join(", ", orParts)}");

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
                            //Console.WriteLine($"Collected negated term: {term}");
                        }
                        else
                        {
                            // Collect positive tokens (tokens that must appear)
                            positiveTerms.Add(trimmed);
                            //Console.WriteLine($"Collected positive term: {trimmed}");
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
                    //Console.WriteLine($"Processed negation: {inner} -> {negatedRegex}");
                    return negatedRegex;
                }
                else
                {
                    // Handle simple positive term within parentheses
                    string positiveRegex = $"(?=.*\\b{Regex.Escape(inner)}\\b)";
                    //Console.WriteLine($"Processed parenthesized term: {inner} -> {positiveRegex}");
                    return positiveRegex;
                }
            }
            else if (expression.StartsWith("!"))
            {
                // Handle simple negation outside of parentheses
                string term = expression.Substring(1).Trim();
                string negatedRegex = $"(?!.*\\b{Regex.Escape(term)}\\b)";
                //Console.WriteLine($"Processed negation: {expression} -> {negatedRegex}");
                return negatedRegex;
            }
            else
            {
                // Handle simple term outside of parentheses
                string positiveRegex = $"(?=.*\\b{Regex.Escape(expression)}\\b)";
                //Console.WriteLine($"Processed term: {expression} -> {positiveRegex}");
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
                    return;

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

                        var btnText = $"🖼️ (View Image)";


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

        public static async Task<(bool isSafe, string? reasonMsg)> PerformSafetyChecks(FoxQueue q)
        {
            var cachedSafetyStatus = await SafetyPromptCache.GetStateAsync(q);

            bool isSafe = true;
            string? reasonStr = null;

            switch (cachedSafetyStatus)
            {
                case SafetyPromptCache.SafetyState.FALSE_POSITIVE:
                case SafetyPromptCache.SafetyState.SAFE:
                    return (true, null);
                case SafetyPromptCache.SafetyState.UNSAFE:
                    reasonStr = "This prompt is known to potentially generate underage human characters, which is against our usage policy.";
                    isSafe = false;
                    break;
            }

            var outputImage = await q.GetOutputImage();

            if (isSafe)
            {
                var imageTags = await outputImage.GetImageTagsAsync();

                if (!FoxContentFilter.ImageTagsSafetyCheck(imageTags))
                {
                    // Start these tasks immediately
                    //var embeddingsTask = DoEmbeddingAndStoreAsync(q);
                    var llmModerationTask = FoxContentFilter.CheckUserIntentAsync(q);

                    var t = q.Telegram;

                    try
                    {
                        if (t is not null && q.MessageID != 0)
                        {
                            await t.EditMessageAsync(
                                id: q.MessageID,
                                text: $"⏳ Performing additional safety checks..."
                            );
                        }
                    }
                    catch { } //We don't care if editing fails.

                    // Now await all tasks to finish

                    try
                    {
                        var llmResults = await llmModerationTask;

                        if (!string.IsNullOrEmpty(llmResults.user_message))
                            reasonStr = llmResults.user_message;

                        var debugMsg = $"LLM moderation result for {q.User.UID}:{q.ID}: {JsonConvert.SerializeObject(llmResults, Formatting.Indented)}";

                        FoxLog.WriteLine(debugMsg + $"\n\nimageTags: { JsonConvert.SerializeObject(imageTags, Formatting.Indented)}");

                        if (!llmResults.violation && llmResults.confidence > 5 && llmResults.intent != FoxContentFilter.ModerationIntent.Deliberate)
                        {
                            // Considered a false positive
                            //await SafetyPromptCache.SaveStateAsync(q, SafetyPromptCache.SafetyState.FALSE_POSITIVE);

                            
                            return (true, reasonStr);
                        }
                        else if (llmResults.violation && llmResults.confidence > 6 && llmResults.intent == ModerationIntent.Deliberate)
                        {
                            await FoxContentFilter.SendModerationNotification(debugMsg);
                            await FoxContentFilter.RecordViolationsAsync(q.ID, new List<ulong> { 0 });
                            await SafetyPromptCache.SaveStateAsync(q, SafetyPromptCache.SafetyState.UNSAFE);
                        }
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex, "Error during LLM-based moderation of {q.User.UID}:{q.ID}: " + ex.Message);
                    }

                    isSafe = false;
                }
            }

            if (!isSafe) {
                FoxLog.WriteLine($"Task {q.ID} - Image failed safety check; cancelling.");
                await q.SetCancelled(true);

                outputImage.Flagged = true;
                await outputImage.Save();
            }

            return (isSafe, reasonStr);
        }

        internal static class SafetyPromptCache
        {
            public enum SafetyState
            {
                UNKNOWN,
                SAFE,
                UNSAFE,
                FALSE_POSITIVE
            }

            public static async Task<SafetyState> GetStateAsync(FoxQueue q)
            {
                var prompt = q.Settings.Prompt + q.Settings.NegativePrompt + q.Settings.ModelName;

                if (prompt is null)
                    throw new ArgumentNullException(nameof(prompt));

                byte[] hash;
                using (var sha = SHA1.Create())
                    hash = sha.ComputeHash(Encoding.UTF8.GetBytes(prompt));

                try
                {
                    using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                    await conn.OpenAsync();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT state FROM safety_prompt_cache WHERE hash = @hash LIMIT 1;";
                    cmd.Parameters.Add("@hash", MySqlDbType.Binary, 20).Value = hash;

                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                        return SafetyState.UNKNOWN;

                    var stateStr = result.ToString() ?? "UNKNOWN";
                    return Enum.TryParse(stateStr, out SafetyState parsed)
                        ? parsed
                        : SafetyState.UNKNOWN;
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, $"SafetyPromptCache.GetSafetyStateAsync: {ex.Message}");
                    return SafetyState.UNKNOWN;
                }
            }

            public static async Task SaveStateAsync(FoxQueue q, SafetyState state)
            {
                var prompt = q.Settings.Prompt + q.Settings.NegativePrompt + q.Settings.ModelName;

                if (prompt is null)
                    throw new ArgumentNullException(nameof(prompt));

                byte[] hash;
                using (var sha = SHA1.Create())
                    hash = sha.ComputeHash(Encoding.UTF8.GetBytes(prompt));

                try
                {
                    using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                    await conn.OpenAsync();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO safety_prompt_cache (hash, state)
                        VALUES (@hash, @state)
                        ON DUPLICATE KEY UPDATE state = VALUES(state);
                    ";

                    cmd.Parameters.Add("@hash", MySqlDbType.Binary, 20).Value = hash;
                    cmd.Parameters.Add("@state", MySqlDbType.Enum).Value = state.ToString();

                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, $"SafetyPromptCache.SaveSafetyStateAsync: {ex.Message}");
                }
            }
        }

    }
}