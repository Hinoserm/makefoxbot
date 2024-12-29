using EmbedIO;
using MySqlConnector;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxWebQueue
    {
        [WebFunctionName("List")]
        [WebLoginRequired(true)]
        [WebAccessLevel(AccessLevel.BASIC)]
        public static async Task<JsonObject?> List(FoxWebSession session, JsonObject jsonMessage)
        {
            long? id = FoxJsonHelper.GetLong(jsonMessage, "ID", true);
            long? uid = FoxJsonHelper.GetLong(jsonMessage, "UID", true);
            string? action = FoxJsonHelper.GetString(jsonMessage, "action", true);
            long? lastImageId = FoxJsonHelper.GetLong(jsonMessage, "lastImageId", true); // Reference ID
            int pageSize = FoxJsonHelper.GetInt(jsonMessage, "PageSize", true) ?? 50;
            string? statusFilter = FoxJsonHelper.GetString(jsonMessage, "Status", true);
            string? modelFilter = FoxJsonHelper.GetString(jsonMessage, "Model", true);

            if (pageSize < 1)
            {
                throw new ArgumentException("Invalid page size.");
            }

            if (uid is not null && uid < 0)
                uid = null;

            if (!session.user!.CheckAccessLevel(AccessLevel.ADMIN))
            {
                // Ensure non-admin can only view their own queue
                if (uid.HasValue)
                {
                    throw new ArgumentException("Only admins can view other users' queues.");
                }
                else
                {
                    // If not specified, show the queue of the current user.
                    uid = (long?)session.user!.UID;
                }
            }

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            var queueItems = new List<FoxQueue>();

            var query = new StringBuilder();
            query.Append("SELECT id FROM queue WHERE 1=1");

            if (id.HasValue)
            {
                query.Append(" AND id = @id");
            }

            if (uid.HasValue)
            {
                query.Append(" AND uid = @uid");
            }

            if (!string.IsNullOrEmpty(modelFilter))
            {
                query.Append(" AND model = @model");
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query.Append(" AND status = @status");
            }

            // Handle 'action' and 'lastImageId' for pagination
            if (!string.IsNullOrEmpty(action) && lastImageId.HasValue)
            {
                if (action == "new")
                {
                    query.Append(" AND id > @lastImageId");
                    query.Append(" ORDER BY id ASC"); // Newer images first
                }
                else if (action == "old")
                {
                    query.Append(" AND id < @lastImageId");
                    query.Append(" ORDER BY id DESC"); // Older images first
                }
                else
                {
                    throw new ArgumentException("Invalid action parameter.");
                }
            }
            else
            {
                // Default ordering
                query.Append(" ORDER BY date_added DESC");
            }

            query.Append(" LIMIT @limit");

            using (var cmd = new MySqlCommand(query.ToString(), SQL))
            {
                if (id.HasValue)
                {
                    cmd.Parameters.AddWithValue("@id", id.Value);
                }

                if (uid.HasValue)
                {
                    cmd.Parameters.AddWithValue("@uid", uid.Value);
                }

                if (!string.IsNullOrEmpty(modelFilter))
                {
                    cmd.Parameters.AddWithValue("@model", modelFilter);
                }

                if (!string.IsNullOrEmpty(statusFilter))
                {
                    cmd.Parameters.AddWithValue("@status", statusFilter);
                }

                if (!string.IsNullOrEmpty(action) && lastImageId.HasValue)
                {
                    cmd.Parameters.AddWithValue("@lastImageId", lastImageId.Value);
                }

                cmd.Parameters.AddWithValue("@limit", pageSize);

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var queueItemId = reader.GetUInt64("id");
                    var queueItem = await FoxQueue.Get(queueItemId);
                    if (queueItem != null)
                    {
                        queueItems.Add(queueItem);
                    }
                }
            }

            var jsonArray = new JsonArray();

            foreach (var item in queueItems)
            {
                var jsonItem = new JsonObject
                {
                    ["ID"] = item.ID,
                    ["Type"] = item.Type.ToString(),
                    ["Status"] = item.status.ToString(),
                    ["UID"] = item.User?.UID,
                    ["Username"] = item.User?.Username,
                    ["Firstname"] = item.Telegram?.User.first_name,
                    ["Lastname"] = item.Telegram?.User.last_name,
                    ["TeleChatID"] = item.Telegram?.Chat?.ID,
                    ["TeleID"] = item.Telegram?.User?.ID,
                    ["Prompt"] = item.Settings.prompt,
                    ["NegativePrompt"] = item.Settings.negative_prompt,
                    ["HiresEnabled"] = item.Settings.hires_enabled,
                    ["HiresWidth"] = item.Settings.hires_width,
                    ["HiresHeight"] = item.Settings.hires_height,
                    ["Width"] = item.Settings.width,
                    ["Height"] = item.Settings.height,
                    ["Steps"] = item.Settings.steps,
                    ["CFGScale"] = item.Settings.cfgscale,
                    ["DenoisingStrength"] = item.Settings.denoising_strength,
                    ["Model"] = item.Settings.model,
                    ["Seed"] = item.Settings.seed,
                    ["Sampler"] = item.Settings.sampler,
                    ["WorkerName"] = await FoxWorker.GetWorkerName(item.WorkerID),
                    ["ImageID"] = item.OutputImageID,
                    ["DateCreated"] = item.DateCreated,
                    ["DateStarted"] = item.DateStarted,
                    ["DateSent"] = item.DateSent,
                    ["DateFinished"] = item.DateFinished
                };

                jsonArray.Add(jsonItem);
            }

            var response = new JsonObject
            {
                ["Command"] = "Queue:List",
                ["Success"] = true,
                ["QueueItems"] = jsonArray
            };

            return response;
        }
    }
}
