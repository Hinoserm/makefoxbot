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

            var messageText = await BuildInfoString(q, true, true);

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

        public static async Task<string> BuildInfoString(FoxQueue q, bool showId = false, bool showDate = false)
        {
            System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
            System.TimeSpan GPUTime = await q.GetGPUTime();

            var sizeString = $"🖥️ Size: {q.Settings.width}x{q.Settings.height}";

            var sb = new StringBuilder();

            sb.AppendLine($"ID: {q.ID}");

            sb.AppendLine($"🖤Prompt: {q.Settings.prompt}");
            sb.AppendLine($"🐊Negative: {q.Settings.negative_prompt}");
            sb.AppendLine(sizeString);
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
    }
}
