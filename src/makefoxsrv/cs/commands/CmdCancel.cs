using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.commands
{
    internal class FoxCmdCancel
    {
        [BotCommand(cmd: "cancel")]
        [CommandDescription("Cancel all pending requests.")]
        public static async Task CmdCancel(FoxTelegram t, FoxUser user, TL.Message message)
        {
            int count = 0;

            var cancelMsg = await t.SendMessageAsync(
                text: $"⏳ Cancelling...",
                replyToMessage: message
            );

            List<ulong> pendingIds = new List<ulong>();

            var matchingItems = FoxQueue.Cache.FindAll(item => !item.IsFinished() && item.User?.UID == user.UID);

            foreach (var q in matchingItems)
            {
                int msg_id = q.MessageID;

                await q.Cancel();

                try
                {
                    _ = t.EditMessageAsync(
                        id: msg_id,
                        text: "❌ Cancelled."
                    );
                }
                catch (Exception ex)
                {
                    //Don't care about this failure.
                    FoxLog.LogException(ex, $"Failed to edit message {msg_id.ToString()}: {ex.Message}");
                }

                count++;
            }

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'CANCELLED' WHERE uid = @uid AND (status = 'PENDING' OR status = 'ERROR' OR status = 'PROCESSING')";
                    cmd.Parameters.AddWithValue("uid", user.UID);
                    count += await cmd.ExecuteNonQueryAsync();
                }
            }

            await t.EditMessageAsync(
                text: $"✅ Cancelled {count} items.",
                id: cancelMsg.ID
            );
        }
    }
}
