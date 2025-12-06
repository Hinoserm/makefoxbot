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
#nullable enable

        public static async Task<string> BuildQueryInfoString(FoxUser user, FoxQueue q, bool showId = false, bool showDate = false)
        {
            TimeSpan gpuTime = await q.GetGPUTime();

            string sizeString = $"{q.Settings.Width}x{q.Settings.Height}";

            // Optional worker name
            string? workerName = null;
            if (q.WorkerID is not null)
            {
                workerName = await FoxWorker.GetWorkerName(q.WorkerID);
            }

            // Optional ID
            ulong? id = showId ? q.ID : null;

            // Optional date string
            string? date = null;
            if (showDate)
            {
                TimeZoneInfo localZone = TimeZoneInfo.Local;
                string timezoneAbbr = GetTimezoneAbbreviation(localZone);
                date = $"{q.DateCreated:MMMM d yyyy hh:mm:ss tt} {timezoneAbbr}";
            }

            // Denoising only for IMG2IMG
            decimal? denoisingStrength = q.Type == FoxQueue.QueueType.IMG2IMG
                ? q.Settings.DenoisingStrength
                : null;

            // Variation seed / strength
            int? variationPercent = null;
            if (q.Settings.variation_seed is not null && q.Settings.variation_strength is not null)
            {
                variationPercent = (int)(q.Settings.variation_strength.Value * 100);
            }

            return user.Strings.Get("Query.Info", new
            {
                Id = id,
                Prompt = q.Settings.Prompt,
                NegativePrompt = q.Settings.NegativePrompt,
                Size = sizeString,
                Sampler = q.Settings.Sampler,
                Steps = q.Settings.steps,
                CfgScale = q.Settings.CFGScale,
                DenoisingStrength = denoisingStrength,
                ModelName = q.Settings.ModelName,
                Seed = q.Settings.Seed,
                VariationSeed = q.Settings.variation_seed,
                VariationPercent = variationPercent,
                WorkerName = workerName,
                RenderTime = gpuTime.ToPrettyFormat(),
                Date = date
            });
        }


        public static async Task<string> BuildUserInfoString(FoxUser user)
        {


            var sb = new StringBuilder();

            sb.AppendLine($"UID: {user.UID}");
            if (user.Username is not null)
                sb.AppendLine($"Username: {user.Username}");
            sb.AppendLine($"Access Level: {user.GetAccessLevel()}");

            ulong imageCount = 0;
            ulong imageBytes = 0;

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
                        imageCount = reader.IsDBNull(reader.GetOrdinal("image_count")) ? 0 : reader.GetUInt64("image_count");
                        imageBytes = reader.IsDBNull(reader.GetOrdinal("image_bytes")) ? 0 : reader.GetUInt64("image_bytes");
                    }
                }
            }

            decimal totalPaid = await user.GetTotalPaid();

            sb.AppendLine($"Images Generated: {imageCount} ({FormatBytes(imageBytes)})");

            if (totalPaid > 0)
                sb.AppendLine($"Paid: ${totalPaid:F2}");

            var floodWait = await user.GetFloodWait();

            if (floodWait > DateTime.Now)
                sb.AppendLine($"FLOOD_WAIT until {floodWait}");

            (decimal InputCost, decimal OutputCost, decimal TotalCost, ulong InputTokens, ulong OutputTokens) = await FoxLLM.CalculateUserLLMCostAsync(user);

            if (InputTokens + OutputTokens > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"LLM Usage: {InputTokens} input tokens, {OutputTokens} output tokens");
                sb.AppendLine($"LLM Cost: ${TotalCost:F4}");
            }

            return sb.ToString();
        }

        public static string FormatBytes(ulong bytes)
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
