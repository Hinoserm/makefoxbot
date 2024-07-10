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
        [WebAccessLevel(AccessLevel.ADMIN)]
        public static async Task<JsonObject?> List(FoxWebSession session, JsonObject jsonMessage)
        {
            long? uid = FoxJsonHelper.GetLong(jsonMessage, "UID", true);
            int pageNumber = FoxJsonHelper.GetInt(jsonMessage, "PageNumber", true) ?? 1;
            int pageSize = FoxJsonHelper.GetInt(jsonMessage, "PageSize", true) ?? 50;
            string? statusFilter = FoxJsonHelper.GetString(jsonMessage, "Status", true);
            string? typeFilter = FoxJsonHelper.GetString(jsonMessage, "Type", true);

            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ArgumentException("Invalid pagination parameters.");
            }

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            var queueItems = new List<FoxQueue>();

            var query = new StringBuilder();
            query.Append("SELECT id FROM queue WHERE 1=1");

            if (uid.HasValue)
            {
                query.Append(" AND uid = @uid");
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query.Append(" AND status = @status");
            }

            if (!string.IsNullOrEmpty(typeFilter))
            {
                query.Append(" AND type = @type");
            }

            query.Append(" ORDER BY date_added DESC");
            query.Append(" LIMIT @limit OFFSET @offset");

            using (var cmd = new MySqlCommand(query.ToString(), SQL))
            {
                if (uid.HasValue)
                {
                    cmd.Parameters.AddWithValue("@uid", uid.Value);
                }

                if (!string.IsNullOrEmpty(statusFilter))
                {
                    cmd.Parameters.AddWithValue("@status", statusFilter);
                }

                if (!string.IsNullOrEmpty(typeFilter))
                {
                    cmd.Parameters.AddWithValue("@type", typeFilter);
                }

                cmd.Parameters.AddWithValue("@limit", pageSize);
                cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);

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
                ["PageNumber"] = pageNumber,
                ["PageSize"] = pageSize,
                ["QueueItems"] = jsonArray
            };

            return response;
        }
    }
}
