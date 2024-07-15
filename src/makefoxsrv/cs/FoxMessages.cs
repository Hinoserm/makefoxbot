﻿using System;
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
using Terminal.Gui;


//This class deals with building and sending Telegram messages to the user.

namespace makefoxsrv
{
    internal class FoxMessages
    {
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

            sb.AppendLine($"🖤Prompt: {q.Settings.prompt}");
            sb.AppendLine($"🐊Negative: {q.Settings.negative_prompt}");
            sb.AppendLine($"🖥️ Size: {q.Settings.width}x{q.Settings.height}");
            sb.AppendLine($"🪜Sampler: {q.Settings.sampler} ({q.Settings.steps} steps)");
            sb.AppendLine($"🧑‍🎨CFG Scale: {q.Settings.cfgscale}");
            sb.AppendLine($"👂Denoising Strength: {q.Settings.denoising_strength}");
            sb.AppendLine($"🧠Model: {q.Settings.model}");
            sb.AppendLine($"🌱Seed: {q.Settings.seed}");

            if (q.WorkerID is not null)
            {
                string workerName = await FoxWorker.GetWorkerName(q.WorkerID) ?? "(unknown)";
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


        public static async Task SendTerms(FoxTelegram t, FoxUser user, int replyMessageID = 0, int editMessage = 0)
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
                    await t.EditMessageAsync(
                        id: editMessage,
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                else
                {
                    await t.SendMessageAsync(
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessageId: replyMessageID

                    );
                }
            }
            catch (WTelegram.WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // We don't care if the message is not modified
            }
        }

        public static async Task SendWelcome(FoxTelegram t, FoxUser user, int replyMessageID = 0, int editMessage = 0)
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
                    await t.EditMessageAsync(
                        id: editMessage,
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                else
                {
                    await t.SendMessageAsync(
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessageId: replyMessageID

                    );

                }
            } catch (WTelegram.WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // We don't care if the message is not modified
            }
        }

        public static async Task HandleUncache(FoxTelegram t, Message message, string? argument)
        {
            if (string.IsNullOrEmpty(argument) || argument.ToLower().Split(' ').Contains("all"))
            {
                long usersRemoved = FoxUser.ClearCache();
                long queueRemoved = FoxQueue.ClearCache();

                await t.SendMessageAsync(
                    text: $"✅ Cleared all caches. Removed {usersRemoved + queueRemoved} items.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                var elements = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long totalRemoved = 0;

                foreach (var element in elements)
                {
                    switch (element.ToLower())
                    {
                        case "users":
                        case "user":
                            totalRemoved += FoxUser.ClearCache();
                            break;
                        case "queue":
                            totalRemoved += FoxQueue.ClearCache();
                            break;
                        default:
                            await t.SendMessageAsync(
                                text: $"❌ Unknown cache element: {element}",
                                replyToMessageId: message.ID
                            );
                            return;
                    }
                }

                await t.SendMessageAsync(
                    text: $"✅ Cleared selected caches. Removed {totalRemoved} items.",
                    replyToMessageId: message.ID
                );
            }
        }

        public static async Task HandleBan(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /admin #ban <uid>\r\n  /admin #ban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(new[] { ' ' }, 2, StringSplitOptions.None);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            string? banMessage = args.Length == 2 ? args[1] : null;

            if (banUser.CheckAccessLevel(AccessLevel.PREMIUM) || banUser.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You can't ban an admin or premium user!",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (banUser.GetAccessLevel() == AccessLevel.BANNED)
            {
                await t.SendMessageAsync(
                    text: "❌ User is already banned.",
                    replyToMessageId: message.ID
                );

                return;
            }

            await banUser.Ban(reasonMessage: banMessage);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} banned.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandleUnban(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to unban.\r\n\r\nFormat:\r\n  /admin #unban <uid>\r\n  /admin #unban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(new[] { ' ' }, 2, StringSplitOptions.None);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            string? banMessage = args.Length == 2 ? args[1] : null;

            if (banUser.GetAccessLevel() != AccessLevel.BANNED)
            {
                await t.SendMessageAsync(
                    text: "❌ User isn't currently banned!",
                    replyToMessageId: message.ID
                );

                return;
            }

            await banUser.UnBan(reasonMessage: banMessage);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} unbanned.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandleResetTerms(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID Format:\r\n  /admin #resetterms <uid>\r\n  /admin #resettos @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(new[] { ' ' }, 2, StringSplitOptions.None);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            
            await banUser.SetTermsAccepted(false);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} must now re-agree to the terms on their next command.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandlePremium(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID and a time span. Format:\r\n  /admin #premium <uid> <timespan>\r\n  /admin #premium @username <timespan>",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(new[] { ' ' }, 2, StringSplitOptions.None);

            if (args.Length < 2)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username and a time span.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var premiumUser = await FoxUser.ParseUser(args[0]);

            if (premiumUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            try
            {
                TimeSpan timeSpan = FoxStrings.ParseTimeSpan(args[1]);
                string action = timeSpan.TotalSeconds >= 0 ? "added to" : "subtracted from";

                DateTime oldExpiry = premiumUser.datePremiumExpires ?? DateTime.Now;
                DateTime newExpiry = oldExpiry.Add(timeSpan);

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "UPDATE users SET date_premium_expires = @date_premium_expires WHERE id = @uid";
                        cmd.Parameters.AddWithValue("@date_premium_expires", newExpiry);
                        cmd.Parameters.AddWithValue("@uid", premiumUser.UID);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await premiumUser.SetPremiumDate(newExpiry);

                string responseMessage = $"✅ {Math.Abs(timeSpan.TotalSeconds)} seconds have been {action} user {premiumUser.UID}'s premium membership.\n" +
                                         $"New Expiration Date: {newExpiry}";

                if (newExpiry < DateTime.Now)
                {
                    responseMessage += "\n⚠️ The new expiration date is in the past!";
                }

                await t.SendMessageAsync(
                    text: responseMessage,
                    replyToMessageId: message.ID
                );
            }
            catch (ArgumentException ex)
            {
                await t.SendMessageAsync(
                    text: $"❌ Invalid time span: {ex.Message}",
                    replyToMessageId: message.ID
                );
            }
        }

    }
}
