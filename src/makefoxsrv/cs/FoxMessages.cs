using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using static System.Net.Mime.MediaTypeNames;
using WTelegram;
using makefoxsrv;
using MySqlConnector;
using System.Collections;
using System.Numerics;


//This class deals with building and sending Telegram messages to the user.

namespace makefoxsrv
{
    internal class FoxMessages
    {

        public static async Task<TL.Message?> SendModelList(FoxTelegram t, FoxUser user, TL.Message? replyToMessage, int pageNumber = 1, int editMessageID = 0)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var models = FoxModel.GetAvailableModels().Where(x => x.Value.PageNumber == pageNumber).ToList();

            if (models.Count == 0)
            {
                throw new Exception("No models available.");
            }

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "Default", data = System.Text.Encoding.UTF8.GetBytes("/model default") }
                }
            });

            // Sort the models dictionary by key (model name) alphabetically
            foreach (var model in models.OrderBy(m => m.Key))
            {
                string modelName = model.Key;
                int workerCount = model.Value.GetWorkersRunningModel().Count; // Assuming you want the count of workers per model

                var buttonLabel = (model.Value.IsPremium ? "⭐" : "") + $"{modelName} ({workerCount})";
                var buttonData = $"/model {modelName}"; // Or any unique data you need to pass

                if (modelName == settings.ModelName)
                    buttonLabel += " ✅";

                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = buttonLabel, data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                    }
                });
            }

            if (pageNumber == 1)
            {
                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") },
                        new TL.KeyboardButtonCallback { text = "More 🡪", data = System.Text.Encoding.UTF8.GetBytes("/model more") }
                    }
                });
            }
            else
            {
                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = "🡨 Back", data = System.Text.Encoding.UTF8.GetBytes("/model back") },
                        new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") }
                        
                    }
                });
            }

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            if (editMessageID == 0)
            {
                return await t.SendMessageAsync(
                    text: "Select a model:\r\n\r\n (⭐ = Premium)",
                    replyInlineMarkup: inlineKeyboard,
                    replyToMessage: replyToMessage
                );
            }
            else
            {
                return await t.EditMessageAsync(
                    id: editMessageID,
                    text: "Select a model:\r\n\r\n (⭐ = Premium)",
                    replyInlineMarkup: inlineKeyboard
                );
            }
        }
        public static async Task SendHistory(FoxTelegram t, FoxUser user, string? argument = null, int messageId = 0, bool editMessage = false)
        {

            ulong? infoId = null;

            if (!string.IsNullOrEmpty(argument))
            {
                if (!ulong.TryParse(argument, out ulong parsedInfoId))
                    throw new Exception("Invalid request.");

                infoId = parsedInfoId;
            }

            FoxQueue? q = null;

            if (infoId is null)
            {
                q = await FoxQueue.GetNewestFromUser(user, (t.Peer is not null && (t.User.ID != t.Peer.ID) ? t.Peer?.ID : null));

                if (q is not null)
                    infoId = q.ID;
            }
            else
                q = await FoxQueue.Get(infoId.Value);

            if (q is null)
                throw new Exception($"Error loading ID {infoId}.");

            if (q.Telegram?.User.ID != t.User.ID)
                throw new Exception($"Permission denied when accessing ID {infoId}");

            // Populate prevId and nextId
            ulong? prevId = null;
            ulong? nextId = null;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
    (SELECT id FROM queue WHERE uid = @uid AND status = 'FINISHED' AND (@teleChatId IS NULL OR tele_chatid = @teleChatId) AND id < @currentId ORDER BY id DESC LIMIT 1)
    UNION ALL
    (SELECT id FROM queue WHERE uid = @uid AND status = 'FINISHED' AND (@teleChatId IS NULL OR tele_chatid = @teleChatId) AND id > @currentId ORDER BY id ASC LIMIT 1);";

                    cmd.Parameters.AddWithValue("@uid", user.UID);
                    cmd.Parameters.AddWithValue("@teleChatId", (t.Peer is not null && (t.User.ID != t.Peer.ID) ? t.Peer?.ID : null));
                    cmd.Parameters.AddWithValue("@currentId", infoId);

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows)
                    {
                        if (await reader.ReadAsync())
                        {
                            prevId = reader.IsDBNull(0) ? (ulong?)null : reader.GetUInt64(0);
                        }
                        if (await reader.ReadAsync())
                        {
                            nextId = reader.IsDBNull(0) ? (ulong?)null : reader.GetUInt64(0);
                        }
                    }
                }
            }

            // Construct the inline keyboard buttons and rows
            var inlineKeyboardButtons = new ReplyInlineMarkup()
            {
                rows = new TL.KeyboardButtonRow[]
                {
                    new TL.KeyboardButtonRow {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new TL.KeyboardButtonCallback { text = "< Prev", data = System.Text.Encoding.ASCII.GetBytes("/history " + (prevId ?? q.ID)) },
                            new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.ID) },
                            new TL.KeyboardButtonCallback { text = "Next >", data = System.Text.Encoding.ASCII.GetBytes("/history " + (nextId ?? q.ID)) },
                        }
                    },
                }
            };

            var messageText = await BuildQueryInfoString(q, true, true);

            if (editMessage)
            {
                await t.EditMessageAsync(
                    text: messageText,
                    id: messageId,
                    replyInlineMarkup: inlineKeyboardButtons
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: messageText,
                    replyToMessageId: messageId,
                    replyInlineMarkup: inlineKeyboardButtons
                );
            }
        }

        public static async Task<string> BuildQueryInfoString(FoxQueue q, bool showId = false, bool showDate = false)
        {
            System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
            System.TimeSpan GPUTime = await q.GetGPUTime();

            var sb = new StringBuilder();

            sb.AppendLine($"ID: {q.ID}");

            sb.AppendLine($"🖤Prompt: {q.Settings.Prompt}");
            sb.AppendLine($"🐊Negative: {q.Settings.NegativePrompt}");
            sb.AppendLine($"🖥️ Size: {q.Settings.Width}x{q.Settings.Height}");
            sb.AppendLine($"🪜Sampler: {q.Settings.Sampler} ({q.Settings.steps} steps)");
            sb.AppendLine($"🧑‍🎨CFG Scale: {q.Settings.CFGScale}");
            sb.AppendLine($"👂Denoising Strength: {q.Settings.DenoisingStrength}");
            sb.AppendLine($"🧠Model: {q.Settings.ModelName}");
            sb.AppendLine($"🌱Seed: {q.Settings.Seed}");

            if (q.WorkerID is not null)
            {
                string workerName = await FoxWorker.GetWorkerName(q.WorkerID);
                sb.AppendLine($"👷Worker: {workerName}");
            }

            sb.AppendLine($"⏳Render Time: {GPUTime.ToPrettyFormat()}");

            if (showDate)
            {
                // Get the server's timezone abbreviation
                TimeZoneInfo localZone = TimeZoneInfo.Local;
                string timezoneAbbr = GetTimezoneAbbreviation(localZone);
                sb.AppendLine($"📅Date: {q.DateCreated.ToString("MMMM d yyyy hh:mm:ss tt")} {timezoneAbbr}");
            }

            return sb.ToString();
        }

        public static async Task<string> BuildUserInfoString(FoxUser user)
        {


            var sb = new StringBuilder();

            sb.AppendLine($"UID: {user.UID}");
            if (user.Username is not null)
                sb.AppendLine($"Username: {user.Username}");
            sb.AppendLine($"Access Level: {user.GetAccessLevel()}");

            long imageCount = 0;
            long imageBytes = 0;
            decimal totalPaid = 0m;

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                MySqlCommand sqlcmd;

                sqlcmd = new MySqlCommand("SELECT COUNT(id) as image_count, SUM(filesize) as image_bytes FROM images WHERE user_id = @uid AND type = 'OUTPUT'", connection);
                sqlcmd.Parameters.AddWithValue("@uid", user.UID);

                using (var reader = await sqlcmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        imageCount = reader.IsDBNull(reader.GetOrdinal("image_count")) ? 0 : reader.GetInt64("image_count");
                        imageBytes = reader.IsDBNull(reader.GetOrdinal("image_bytes")) ? 0 : reader.GetInt64("image_bytes");
                    }
                }

                sqlcmd = new MySqlCommand("SELECT SUM(amount) as total_paid FROM user_payments WHERE uid = @uid", connection);
                sqlcmd.Parameters.AddWithValue("@uid", user.UID);

                using (var reader = await sqlcmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        totalPaid = reader.IsDBNull(reader.GetOrdinal("total_paid")) ? 0 : (reader.GetInt64("total_paid") / 100.0m);
                    }
                }
            }

            sb.AppendLine($"Images Generated: {imageCount} ({FormatBytes(imageBytes)})");

            if (totalPaid > 0)
                sb.AppendLine($"Paid: ${totalPaid:F2}");

            var floodWait = await user.GetFloodWait();

            if (floodWait > DateTime.Now)
                sb.AppendLine($"FLOOD_WAIT until {floodWait}");

            return sb.ToString();
        }

        public static string FormatBytes(long bytes)
        {
            const long KiloByte = 1024;
            const long MegaByte = KiloByte * 1024;
            const long GigaByte = MegaByte * 1024;
            const long TeraByte = GigaByte * 1024;
            const long PetaByte = TeraByte * 1024;
            const long ExaByte = PetaByte * 1024;

            if (bytes < KiloByte)
            {
                return $"{bytes} B";
            }
            else if (bytes < MegaByte)
            {
                return $"{bytes / (double)KiloByte:F2} KB";
            }
            else if (bytes < GigaByte)
            {
                return $"{bytes / (double)MegaByte:F2} MB";
            }
            else if (bytes < TeraByte)
            {
                return $"{bytes / (double)GigaByte:F2} GB";
            }
            else if (bytes < PetaByte)
            {
                return $"{bytes / (double)TeraByte:F2} TB";
            }
            else if (bytes < ExaByte)
            {
                return $"{bytes / (double)PetaByte:F2} PB";
            }
            else
            {
                return $"{bytes / (double)ExaByte:F2} EB";
            }
        }

        // Helper method to get timezone abbreviation
        private static string GetTimezoneAbbreviation(TimeZoneInfo timeZone)
        {
            var now = DateTime.Now;
            var offset = timeZone.GetUtcOffset(now);
            var isDaylight = timeZone.IsDaylightSavingTime(now);
            var abbreviation = new StringBuilder();

            switch (timeZone.Id)
            {
                case "Central Standard Time":
                    abbreviation.Append(isDaylight ? "CDT" : "CST");
                    break;
                // Add other time zones as needed
                default:
                    abbreviation.Append(isDaylight ? timeZone.DaylightName : timeZone.StandardName);
                    break;
            }

            return abbreviation.ToString();
        }


        public static async Task<Message?> SendTerms(FoxTelegram t, FoxUser user, TL.Message? replyToMessage = null, int editMessage = 0)
        {
            try
            {
                TL.ReplyInlineMarkup? inlineKeyboardButtons = null;

                if (user.DateTermsAccepted is null)
                {
                    inlineKeyboardButtons = new TL.ReplyInlineMarkup
                    {
                        rows = new TL.KeyboardButtonRow[]
                        {
                            new TL.KeyboardButtonRow
                            {
                                buttons = new TL.KeyboardButtonCallback[]
                                {
                                    new TL.KeyboardButtonCallback { text = user.Strings.Get("Terms.AgreeButton"), data = System.Text.Encoding.ASCII.GetBytes("/terms agree") },
                                }
                            }
                        }
                    };
                }

                var message = user.Strings.Get("Terms.Message");

                if (user.DateTermsAccepted is null)
                {
                    message += "\n\n" + user.Strings.Get("Terms.AgreePrompt");
                }
                else
                {
                    message += "\n\n" + user.Strings.Get("Terms.UserAgreed");
                }

                var entities = FoxTelegram.Client.HtmlToEntities(ref message);

                if (editMessage != 0)
                {
                    return await t.EditMessageAsync(
                        id: editMessage,
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                else
                {
                    var sentMsg = await t.SendMessageAsync(
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessage: replyToMessage

                    );

                    if (sentMsg is not null && t.User.ID == t.Peer?.ID) // Don't try to pin in groups.
                        await t.PinMessage(sentMsg.ID);

                    return sentMsg;
                }
            }
            catch (WTelegram.WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // We don't care if the message is not modified
            }

            return null;
        }

        public static async Task<Message?> SendWelcome(FoxTelegram t, FoxUser user, TL.Message? replyToMessage = null, int editMessage = 0)
        {
            try
            {
                var inlineKeyboardButtons = user.Strings.GenerateLanguageButtons();

                if (user.DateTermsAccepted is null)
                {
                    inlineKeyboardButtons.rows = inlineKeyboardButtons.rows.Concat(new TL.KeyboardButtonRow[]
                    {
                        new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = user.Strings.Get("Terms.AgreeButton"), data = System.Text.Encoding.ASCII.GetBytes("/terms agree") },
                            }
                        }
                    }).ToArray();
                }

                var message = user.Strings.Get("Welcome");

                if (user.DateTermsAccepted is null)
                {
                    message += "\n\n" + user.Strings.Get("Terms.AgreePrompt");
                }
                else
                {
                    message += "\n\n" + user.Strings.Get("Terms.UserAgreed");
                }

                var entities = FoxTelegram.Client.HtmlToEntities(ref message);

                if (editMessage != 0)
                {
                    return await t.EditMessageAsync(
                        id: editMessage,
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                else
                {
                    var sentMsg = await t.SendMessageAsync(
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessage: replyToMessage

                    );

                    if (sentMsg is not null && t.User.ID == t.Peer?.ID) // Don't try to pin in groups.
                        await t.PinMessage(sentMsg.ID);

                    return sentMsg;
                }
            } catch (WTelegram.WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // We don't care if the message is not modified
            }

            return null;
        }
    }
}
