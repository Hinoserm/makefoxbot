using EmbedIO;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxWebQueue
    {
        // map public filter names (PascalCase) to SQL column names (snake_case)
        static readonly Dictionary<string, string> NormalizedFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = "id",
            ["UID"] = "uid",
            ["Status"] = "status",
            ["Model"] = "model",
            ["Sampler"] = "sampler",
            ["Steps"] = "steps",
            ["Seed"] = "seed",
            ["CFGScale"] = "cfgscale",
            ["Prompt"] = "prompt",
            ["NegativePrompt"] = "negative_prompt",
            ["Width"] = "width",
            ["Height"] = "height",
            ["DenoisingStrength"] = "denoising_strength",
            ["HiresWidth"] = "hires_width",
            ["HiresHeight"] = "hires_height",
            ["HiresSteps"] = "hires_steps",
            ["HiresDenoisingStrength"] = "hires_denoising_strength",
            ["HiresEnabled"] = "hires_enabled",
            ["HiresUpscaler"] = "hires_upscaler",
            ["VariationSeed"] = "variation_seed",
            ["VariationStrength"] = "variation_strength",
            ["SelectedImage"] = "selected_image",
            ["Enhanced"] = "enhanced",
            ["Complexity"] = "complexity"
        };

        // map normalized SQL names to in-memory accessors
        static readonly Dictionary<string, Func<FoxQueue, object?>> QueueFieldAccessors = new Dictionary<string, Func<FoxQueue, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = q => q.ID,
            ["uid"] = q => q.User?.UID,
            ["status"] = q => q.status.ToString(),
            ["model"] = q => q.Settings.ModelName,
            ["sampler"] = q => q.Settings.Sampler,
            ["steps"] = q => q.Settings.steps,
            ["seed"] = q => q.Settings.Seed,
            ["cfgscale"] = q => q.Settings.CFGScale,
            ["prompt"] = q => q.Settings.Prompt,
            ["negative_prompt"] = q => q.Settings.NegativePrompt,
            ["width"] = q => q.Settings.Width,
            ["height"] = q => q.Settings.Height,
            ["denoising_strength"] = q => q.Settings.DenoisingStrength,
            ["hires_width"] = q => q.Settings.hires_width,
            ["hires_height"] = q => q.Settings.hires_height,
            ["hires_steps"] = q => q.Settings.hires_steps,
            ["hires_denoising_strength"] = q => q.Settings.hires_denoising_strength,
            ["hires_enabled"] = q => q.Settings.hires_enabled,
            ["hires_upscaler"] = q => q.Settings.hires_upscaler,
            ["variation_seed"] = q => q.Settings.variation_seed,
            ["variation_strength"] = q => q.Settings.variation_strength,
            ["selected_image"] = q => q.Settings.SelectedImage,
            ["enhanced"] = q => q.Enhanced,
            ["complexity"] = q => q.Complexity
        };

        [WebFunctionName("List")]
        [WebLoginRequired(true)]
        [WebAccessLevel(AccessLevel.BASIC)]
        public static async Task<JsonObject?> List(FoxWebSession session, JsonObject jsonMessage)
        {
            long? id = FoxJsonHelper.GetLong(jsonMessage, "ID", true);
            long? uid = FoxJsonHelper.GetLong(jsonMessage, "UID", true);
            string? action = FoxJsonHelper.GetString(jsonMessage, "action", true);
            long? lastImageId = FoxJsonHelper.GetLong(jsonMessage, "lastImageId", true);
            int pageSize = FoxJsonHelper.GetInt(jsonMessage, "PageSize", true) ?? 50;
            string? statusFilter = FoxJsonHelper.GetString(jsonMessage, "Status", true);
            string? modelFilter = FoxJsonHelper.GetString(jsonMessage, "Model", true);

            JsonObject? filters = jsonMessage["Filters"] as JsonObject;

            if (pageSize < 1)
                throw new ArgumentException("Invalid page size.");

            if (uid is not null && uid < 0)
                uid = null;

            if (!session.user!.CheckAccessLevel(AccessLevel.ADMIN))
            {
                if (uid.HasValue)
                    throw new ArgumentException("Only admins can view other users' queues.");
            }

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            var queueItems = new List<FoxQueue>();
            using (var cmd = new MySqlCommand(SQL, null))
            {
                var query = new StringBuilder("SELECT id FROM queue WHERE 1=1");

                // advanced Filters
                if (filters is not null)
                {
                    foreach (var kv in filters)
                    {
                        var key = kv.Key;
                        var value = kv.Value;
                        if (value is null) continue;

                        if (NormalizedFieldMap.TryGetValue(key, out var column))
                        {
                            FoxFilterHelper.AppendSqlCondition(column, value, query, cmd);
                        }
                        else if (key.EndsWith("Contains", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseKey = key[..^"Contains".Length];
                            if (!NormalizedFieldMap.TryGetValue(baseKey, out var colLike))
                                throw new ArgumentException($"Unsupported filter field: {key}.");

                            var param = $"@p_{cmd.Parameters.Count}";
                            query.Append($" AND {colLike} LIKE {param}");
                            cmd.Parameters.AddWithValue(param, $"%{value.ToString()}%");
                        }
                        else
                        {
                            throw new ArgumentException($"Unsupported filter field: {key}.");
                        }
                    }
                }

                // legacy filters (still applied)
                if (id.HasValue)
                    query.Append(" AND id = @id");
                if (uid.HasValue)
                    query.Append(" AND uid = @uid");
                if (!string.IsNullOrEmpty(modelFilter))
                    query.Append(" AND model = @model");
                if (!string.IsNullOrEmpty(statusFilter))
                    query.Append(" AND status = @status");

                // force non-admin to own UID
                if (!session.user.CheckAccessLevel(AccessLevel.ADMIN))
                {
                    query.Append(" AND uid = @forced_uid");
                    cmd.Parameters.AddWithValue("@forced_uid", session.user.UID);
                }

                // pagination
                if (!string.IsNullOrEmpty(action) && lastImageId.HasValue)
                {
                    if (action == "new")
                    {
                        query.Append(" AND id > @lastImageId ORDER BY id ASC");
                    }
                    else if (action == "old")
                    {
                        query.Append(" AND id < @lastImageId ORDER BY id DESC");
                    }
                    else
                    {
                        throw new ArgumentException("Invalid action parameter.");
                    }
                }
                else
                {
                    query.Append(" ORDER BY date_added DESC");
                }

                query.Append(" LIMIT @limit");

                // bind legacy params
                if (id.HasValue)
                    cmd.Parameters.AddWithValue("@id", id.Value);
                if (uid.HasValue)
                    cmd.Parameters.AddWithValue("@uid", uid.Value);
                if (!string.IsNullOrEmpty(modelFilter))
                    cmd.Parameters.AddWithValue("@model", modelFilter);
                if (!string.IsNullOrEmpty(statusFilter))
                    cmd.Parameters.AddWithValue("@status", statusFilter);
                if (!string.IsNullOrEmpty(action) && lastImageId.HasValue)
                    cmd.Parameters.AddWithValue("@lastImageId", lastImageId.Value);

                cmd.Parameters.AddWithValue("@limit", pageSize);
                cmd.CommandText = query.ToString();

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var queueItemId = reader.GetUInt64("id");
                    var queueItem = await FoxQueue.Get(queueItemId, noCache: true);
                    if (queueItem != null)
                        queueItems.Add(queueItem);
                }
            }

            var jsonArray = new JsonArray();
            foreach (var item in queueItems)
                jsonArray.Add(await BuildQueueJsonItem(item));

            return new JsonObject
            {
                ["Command"] = "Queue:List",
                ["Success"] = true,
                ["QueueItems"] = jsonArray
            };
        }

        [WebFunctionName("SubscribeUpdates")]
        [WebLoginRequired(true)]
        [WebAccessLevel(AccessLevel.BASIC)]
        public static async Task<JsonObject?> SubscribeUpdates(FoxWebSession session, JsonObject jsonMessage)
        {
            var channel = FoxJsonHelper.GetString(jsonMessage, "channel", false);
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("Missing or invalid 'channel'.");

            var filters = jsonMessage["Filters"] as JsonObject;
            if (filters is null)
                throw new ArgumentException("Missing or invalid 'Filters'.");

            // validate
            foreach (var kv in filters)
            {
                var key = kv.Key;
                var value = kv.Value;
                if (value is null)
                    throw new ArgumentException($"Null filter value for: {key}");

                if (NormalizedFieldMap.ContainsKey(key) ||
                    key.EndsWith("Contains", StringComparison.OrdinalIgnoreCase) &&
                    NormalizedFieldMap.ContainsKey(key[..^"Contains".Length]))
                {
                    continue;
                }
                throw new ArgumentException($"Unsupported filter field: {key}.");
            }

            lock (session)
            {
                var existing = session.QueueSubscriptions.FirstOrDefault(s => s.Channel == channel);
                if (existing != null)
                    existing.Filters = filters;
                else
                    session.QueueSubscriptions.Add(new FoxWebSession.QueueSubscription
                    {
                        Channel = channel,
                        Filters = filters
                    });
            }

            return new JsonObject
            {
                ["Command"] = "Queue:SubscribeUpdates",
                ["Success"] = true,
                ["Channel"] = channel
            };
        }

        private static object? GetFieldValue(FoxQueue item, string normalizedKey)
        {
            if (QueueFieldAccessors.TryGetValue(normalizedKey, out var fn))
                return fn(item);
            throw new ArgumentException($"Unsupported field in subscription filter: {normalizedKey}");
        }

        private static async Task<JsonObject> BuildQueueJsonItem(FoxQueue item)
        {
            return new JsonObject
            {
                ["ID"] = item.ID,
                ["Type"] = item.Type.ToString(),
                ["Status"] = item.status.ToString(),
                ["UID"] = item.User?.UID,
                ["Username"] = item.User?.Username,
                ["Firstname"] = item.Telegram?.User.first_name,
                ["Lastname"] = item.Telegram?.User?.last_name,
                ["TeleChatID"] = item.Telegram?.Chat?.ID,
                ["TeleID"] = item.Telegram?.User?.ID,
                ["Prompt"] = item.Settings.Prompt,
                ["NegativePrompt"] = item.Settings.NegativePrompt,
                ["HiresEnabled"] = item.Settings.hires_enabled,
                ["HiresWidth"] = item.Settings.hires_width,
                ["HiresHeight"] = item.Settings.hires_height,
                ["Width"] = item.Settings.Width,
                ["Height"] = item.Settings.Height,
                ["Steps"] = item.Settings.steps,
                ["CFGScale"] = item.Settings.CFGScale,
                ["DenoisingStrength"] = item.Settings.DenoisingStrength,
                ["Model"] = item.Settings.ModelName,
                ["Seed"] = item.Settings.Seed,
                ["Sampler"] = item.Settings.Sampler,
                ["WorkerName"] = await FoxWorker.GetWorkerName(item.WorkerID),
                ["ImageID"] = item.OutputImageID,
                ["DateCreated"] = item.DateCreated,
                ["DateStarted"] = item.DateStarted,
                ["DateSent"] = item.DateSent,
                ["DateFinished"] = item.DateFinished
            };
        }

        public static async Task BroadcastQueueUpdate(FoxQueue item)
        {
            var active = FoxWebSession.GetActiveWebSocketSessions();

            foreach (var (wsContext, session) in active)
            {
                if (!session.user!.CheckAccessLevel(AccessLevel.ADMIN) &&
                    session.user.UID != item.User?.UID)
                    continue;

                foreach (var sub in session.QueueSubscriptions)
                {
                    bool match = true;

                    foreach (var kv in sub.Filters)
                    {
                        var key = kv.Key;
                        var val = kv.Value!;
                        if (NormalizedFieldMap.TryGetValue(key, out var norm))
                        {
                            var actual = GetFieldValue(item, norm);
                            if (!FoxFilterHelper.Matches(val, actual))
                            {
                                match = false;
                                break;
                            }
                        }
                        else if (key.EndsWith("Contains", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseKey = key[..^"Contains".Length];
                            if (!NormalizedFieldMap.TryGetValue(baseKey, out var normLike))
                                throw new ArgumentException($"Unsupported filter field: {key}.");

                            var actual = GetFieldValue(item, normLike)?.ToString() ?? "";
                            if (!actual.Contains(val.ToString() ?? "", StringComparison.OrdinalIgnoreCase))
                            {
                                match = false;
                                break;
                            }
                        }
                        else
                        {
                            throw new ArgumentException($"Unsupported filter field: {key}.");
                        }
                    }

                    if (!match)
                        continue;

                    var payload = await BuildQueueJsonItem(item);
                    var response = new
                    {
                        Command = "Queue:StatusUpdate",
                        Channel = sub.Channel,
                        Payload = payload
                    };

                    var json = JsonSerializer.Serialize(response);
                    var buffer = Encoding.UTF8.GetBytes(json);

                    await wsContext.WebSocket.SendAsync(buffer, true);
                }
            }
        }
    }
}
