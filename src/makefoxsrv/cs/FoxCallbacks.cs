using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace makefoxsrv
{
    internal class FoxCallbacks
    {
        public static async Task Handle(FoxTelegram t, UpdateBotCallbackQuery query, string data)
        {
            var a = data.Split(" ", 2);
            var command = a[0];

            var argument = (a[1] is not null ? a[1] : "");

            var fUser = await FoxUser.GetByTelegramUser(t.User, false);

            if (fUser is null)
                throw new Exception("Callback from unknown user!");

            switch (command)
            {
                case "/info":
                    await CallbackCmdInfo(t, query, fUser, argument);
                    break;
                case "/download":
                    await CallbackCmdDownload(t, query, fUser, argument);
                    break;
                case "/select":
                    await CallbackCmdSelect(t, query, fUser, argument);
                    break;
                case "/model":
                    await CallbackCmdModel(t, query, fUser, argument);
                    break;
                case "/help":
                    await CallbackCmdHelp(t, query, fUser, argument);
                    break;
                case "/donate":
                    await CallbackCmdDonate(t, query, fUser, argument);
                    break;
                case "/lang":
                    await CallbackCmdLanguage(t, query, fUser, argument);
                    break;
                case "/terms":
                    await CallbackCmdTerms(t, query, fUser, argument);
                    break;
                case "/enhance":
                    await CallbackCmdEnhance(t, query, fUser, argument);
                    break;
            }
        }

        private static async Task CallbackCmdEnhance(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out ulong info_id))
            {
                throw new Exception("Malformed request");
            }

            var q = await FoxQueue.Get(info_id);
            if (q is null)
                throw new Exception("Unable to locate queue item");

            if (q.Telegram?.User.ID != t.User.ID)
            {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return; // Just silently return.
            }

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);
            
            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, query.msg_id);

                return; // User must agree to the terms before they can use this command.
            }

            if (user.GetAccessLevel() < AccessLevel.ADMIN)
            {

                if (q.Settings.width >= 1920 || q.Settings.height >= 1920)
                {
                    await t.SendMessageAsync(
                        text: $"❌ This image is already at the maximum allowed resolution!  Enhancing again won't accomplish anything.  Please go back and enhance the original image if you'd like a different result.",
                        replyToMessageId: query.msg_id
                        );

                    return;
                }

                if (user.GetAccessLevel() < AccessLevel.PREMIUM)
                {
                    using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                    {
                        await SQL.OpenAsync();

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.CommandText = "SELECT date_finished FROM queue WHERE uid = @uid AND status = 'FINISHED' AND enhanced = 1 AND date_finished > @now - INTERVAL 30 MINUTE ORDER BY date_finished DESC LIMIT 1"; //INTERVAL 1 HOUR";
                            cmd.Parameters.AddWithValue("uid", user.UID);
                            cmd.Parameters.AddWithValue("now", DateTime.Now);
                            await using var reader = await cmd.ExecuteReaderAsync();

                            if (reader.HasRows && await reader.ReadAsync())
                            {
                                var date = reader.GetDateTime(0);

                                var span = TimeSpan.FromMinutes(30) - (DateTime.Now - date);

                                await t.SendMessageAsync(
                                    text: $"❌ Basic users are limited to 1 enhanced image per 30 minutes.\nTry again after {span.ToPrettyFormat()}.\n\nPlease consider becoming a premium member: /donate",
                                    replyToMessageId: query.msg_id
                                );

                                return;
                            }
                        }
                    }
                }

                int q_limit = 1;

                if (await FoxQueue.GetCount(user) >= q_limit)
                {
                    await t.SendMessageAsync(
                        text: $"❌Maximum of {q_limit} queued enhancement request per user.",
                        replyToMessageId: query.msg_id
                    );

                    return;
                }
            }

            FoxUserSettings settings = q.Settings.Copy();

            (settings.width, settings.height) = FoxImage.CalculateLimitedDimensions(settings.width * 2, settings.height * 2, 1920);

            settings.seed = -1;
            settings.steps = 15;
            settings.denoising_strength = 0.50M;

            settings.selected_image = q.OutputImageID.Value;

            if (FoxQueue.CheckWorkerAvailability(settings) is null)
            {
                await t.SendMessageAsync(
                    text: "❌ No workers available to process this task.\n\nPlease reduce your /size, select a different /model, or try again later.",
                    replyToMessageId: query.msg_id
                );

                return;
            }

            (int position, int totalItems) = FoxQueue.GetNextPosition(user, true);

            Message waitMsg = await t.SendMessageAsync(
                text: $"⏳ Adding to queue ({position} of {totalItems})...",
                replyToMessageId: query.msg_id
            );

            var newq = await FoxQueue.Add(t, user, settings, FoxQueue.QueueType.IMG2IMG, waitMsg.ID, query.msg_id, true, q);
            if (newq is null)
                throw new Exception("Unable to add item to queue");

            await FoxQueue.Enqueue(newq);
        }

        private static async Task CallbackCmdLanguage(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            if (argument is null || argument.Length <= 0)
            {
                argument = "en";
            }

            await user.SetPreferredLanguage(argument);

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, user.Strings.Get("Lang.AnswerCallbackMsg"));

            await FoxMessages.SendWelcome(t, user, 0, query.msg_id);

        }

        private static async Task CallbackCmdTerms(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            if (argument is null || argument.Length <= 0)
            {
                argument = "en";
            }

            await user.SetTermsAccepted();

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, user.Strings.Get("Terms.AgreeClicked"));

            await FoxMessages.SendTerms(t, user, 0, query.msg_id);

        }

        private static async Task CallbackCmdModel(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            if (argument is null || argument.Length <= 0)
            {
                /* await botClient.EditMessageTextAsync(
                    chatId: update.CallbackQuery.Message.Chat.Id,
                    text: "Invalid request",
                    messageId: update.CallbackQuery.Message.MessageId,
                    cancellationToken: cancellationToken
                ); */

                return;
            }

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);

            if (argument == "cancel")
            {
                await t.EditMessageAsync(
                        text: "✅ Operation cancelled.",
                        id: query.msg_id
                    );
            }
            else
            {
                if (argument == "default")
                {
                    argument = null;
                } else if (await FoxWorker.GetWorkersForModel(argument) is null)
                {
                    await t.EditMessageAsync(
                        text: "❌ There are no workers currently available that can handle that model.  Please try again later.",
                        id: query.msg_id
                    );

                    return;
                }

                var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                settings.model = argument;

                await settings.Save();

                await t.EditMessageAsync(
                        text: "✅ Model selected: " + settings.model,
                        id: query.msg_id
                    );
            }
        }

        private static async Task CallbackCmdDonate(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            if (argument is null || argument.Length <= 0)
            {
                /* await botClient.EditMessageTextAsync(
                    chatId: update.CallbackQuery.Message.Chat.Id,
                    text: "Invalid request",
                    messageId: update.CallbackQuery.Message.MessageId,
                    cancellationToken: cancellationToken
                ); */

                return;
            }

            if (argument == "cancel")
            {
                await t.EditMessageAsync(
                        text: "✅ Operation cancelled.",
                        id: query.msg_id
                    );
            }
            else if (argument == "custom")
            {
                await t.EditMessageAsync(
                        text: "🚧 Not Yet Implemented.",
                        id: query.msg_id
                    );
            }
            else
            {
                if (string.IsNullOrEmpty(FoxMain.settings?.TelegramPaymentToken))
                    throw new Exception("Donations are currently disabled. (token not set)");

                var parts = argument.Split(' ');
                if (parts.Length != 2)
                    throw new Exception("Invalid input format.");

                if (!decimal.TryParse(parts[0], out decimal amount) || amount < 5)
                    throw new Exception("Invalid amount.");

                int days;
                if (parts[1].ToLower() == "lifetime")
                    days = -1;
                else if (!int.TryParse(parts[1], out days))
                    throw new Exception("Invalid days.");

                var prices = new TL.LabeledPrice[] {
                    new TL.LabeledPrice { label = days == -1 ? "Lifetime Access" : $"{days} Days Access", amount = (int)(amount * 100) },
                };

                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);

                var inputInvoice = new TL.InputMediaInvoice
                {
                    title = $"One-Time Payment for User ID {user.UID}",
                    description = days == -1 ? "Lifetime Access" : $"{days} Days Access",
                    payload = System.Text.Encoding.UTF8.GetBytes($"PAY_{user.UID}_{days}"),
                    provider = FoxMain.settings?.TelegramPaymentToken, // Make sure this is correctly obtained
                    provider_data = new TL.DataJSON { data = "{\"items\":[{\"description\":\"Product\",\"quantity\":1.0}]}" },
                    invoice = new TL.Invoice
                    {
                        currency = "USD",
                        prices = prices,
                        flags = Invoice.Flags.test
                    }
                };

                var replyMarkup = new TL.ReplyInlineMarkup
                {
                    rows = new TL.KeyboardButtonRow[] {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButton[] {
                                new TL.KeyboardButtonBuy { text = $"Pay ${amount}" }
                            }
                        }
                    }
                };

                try
                {
                    await FoxTelegram.Client.Messages_SendMedia(
                        media: inputInvoice,
                        peer: t.Peer,
                        message: "Thank you!",
                        random_id: Helpers.RandomLong(),
                        reply_markup: replyMarkup
                     );
                }
                catch ( Exception ex )
                {
                    FoxLog.WriteLine("DONATE ERROR: " + ex.Message);
                }



                /* try
                {
                    await botClient.EditMessageTextAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: update.CallbackQuery.Message.Text,
                        messageId: update.CallbackQuery.Message.MessageId,
                        disableWebPagePreview: true,
                        cancellationToken: cancellationToken,
                        entities: update.CallbackQuery.Message.Entities
                    );
                }
                catch { } // We don't care if editing fails */

            }
        }

        private static async Task CallbackCmdInfo(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            ulong info_id = 0;

            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out info_id))
            {
                /* await botClient.EditMessageTextAsync(
                    chatId: update.CallbackQuery.Message.Chat.Id,
                    text: "Invalid request",
                    messageId: update.CallbackQuery.Message.MessageId,
                    cancellationToken: cancellationToken
                ); */

                return;
            }

            var q = await FoxQueue.Get(info_id);
            if (q is null)
                return;

            

            if (q is not null && q.Telegram?.User.ID != t.User.ID) {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Only the original creator can click this button!");
            }
            else 
            {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);
                // Construct the inline keyboard buttons and rows
                var inlineKeyboardButtons = new ReplyInlineMarkup()
                {
                    rows = new TL.KeyboardButtonRow[]
                    {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.ID) },
                                new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.ID) },
                                new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.ID)},
                                new TL.KeyboardButtonCallback { text = "🎨", data = System.Text.Encoding.ASCII.GetBytes("/select " + q.ID)},
                            }
                        },
                        new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "✨ Enhance!", data = System.Text.Encoding.ASCII.GetBytes("/enhance " + q.ID)},
                            }
                        }
                    }
                };

                System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
                System.TimeSpan GPUTime = await q.GetGPUTime();

                //var maxWidth = Math.Max(q.Settings.width, q.Settings.UpscalerWidth ?? 0);
                //var maxHeight = Math.Max(q.Settings.height, q.Settings.UpscalerHeight ?? 0);

                var sizeString = $"🖥️ Size: {q.Settings.width}x{q.Settings.height}";

                //if (q.Settings.UpscalerWidth is not null && q.Settings.UpscalerHeight is not null)
                //    sizeString += $" (upscaled from {q.Settings.width}x{q.Settings.height})";

                await t.EditMessageAsync(
                    text: $"🖤Prompt: {q.Settings.prompt}\r\n" +
                          $"🐊Negative: {q.Settings.negative_prompt}\r\n" +
                          $"{sizeString}\r\n" +
                          $"🪜Sampler Steps: {q.Settings.steps}\r\n" +
                          $"🧑‍🎨CFG Scale: {q.Settings.cfgscale}\r\n" +
                          $"👂Denoising Strength: {q.Settings.denoising_strength}\r\n" +
                          $"🧠Model: {q.Settings.model}\r\n" +
                          $"🌱Seed: {q.Settings.seed}\r\n" +
                          (q.WorkerID is not null ? $"👷Worker: " + (await FoxWorker.GetWorkerName(q.WorkerID) ?? "(unknown)") + "\r\n" : "") +
                          $"⏳Render Time: {GPUTime.ToPrettyFormat()}\r\n",
                    id: query.msg_id,
                    replyInlineMarkup: inlineKeyboardButtons
                );
            }
        }

        private static async Task CallbackCmdHelp(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            int help_id = 1;

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);

            if (argument is null || argument.Length <= 0 || !int.TryParse(argument, out help_id))
                help_id = 1;

            if (help_id < 1 || help_id > FoxStrings.text_help.Count())
                help_id = 1;

            var inlineKeyboardButtons = new ReplyInlineMarkup()
            {
                rows = new TL.KeyboardButtonRow[]
                {
                    new TL.KeyboardButtonRow {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new TL.KeyboardButtonCallback { text = "More Help", data = System.Text.Encoding.ASCII.GetBytes("/help "  + (help_id+1)) },
                        }
                    }
                }
            };

            if (help_id >= FoxStrings.text_help.Count())
                inlineKeyboardButtons = null;

            await t.EditMessageAsync(
                id: query.msg_id,
                replyInlineMarkup: null
            );

            await t.SendMessageAsync(
                text: FoxStrings.text_help[help_id - 1],
                replyInlineMarkup: inlineKeyboardButtons
            );
        }

        private static async Task CallbackCmdDownload(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            ulong info_id = 0;

            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out info_id))
                throw new Exception("Malformed request");

            var q = await FoxQueue.Get(info_id);
            if (q is null)
            {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Error loading image.");
                return;
            }

            if (q.Telegram?.User.ID != t.User.ID)
            {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return;
            }

            await t.SendCallbackAnswer(query.query_id, 0, "Transferring, please wait...");

            var img = await q.GetOutputImage();

            if (img is null)
                throw new Exception("Unable to locate image");

            var inputImage = await FoxTelegram.Client.UploadFileAsync(new MemoryStream(img.Image), $"{FoxTelegram.Client.User.username}_full_image_{q.ID}.png");

            var msg = await FoxTelegram.Client.SendMessageAsync(t.Peer, "", new InputMediaUploadedDocument(inputImage, "image/png"));

            //await img.SaveFullTelegramFileIds(message.Document.FileId, message.Document.FileUniqueId);
        }

        private static async Task CallbackCmdSelect(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

           ulong info_id = 0;

            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out info_id))
                throw new Exception("Malformed request");

            var q = await FoxQueue.Get(info_id);
            if (q is null)
            {
                await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Error loading image.");
                return;
            }

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Selected for img2img");

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (q.OutputImageID is null)
                return;

            settings.selected_image = (ulong)q.OutputImageID;

            await settings.Save();

            //var inlineKeyboardButtons = new ReplyInlineMarkup()
            //{
            //    rows = new TL.KeyboardButtonRow[]
            //    {
            //        new TL.KeyboardButtonRow {
            //            buttons = new TL.KeyboardButtonCallback[]
            //            {
            //                new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.ID) },
            //                new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.ID) },
            //                new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.ID)},
            //                new TL.KeyboardButtonCallback { text = "🎨", data = System.Text.Encoding.ASCII.GetBytes("/select " + q.ID)},
            //            }
            //        },
            //        new TL.KeyboardButtonRow
            //        {
            //            buttons = new TL.KeyboardButtonCallback[]
            //            {
            //                new TL.KeyboardButtonCallback { text = "✨ Enhance!", data = System.Text.Encoding.ASCII.GetBytes("/enhance " + q.ID)},
            //                new TL.KeyboardButtonCallback { text = "Show Details", data = System.Text.Encoding.ASCII.GetBytes("/info " + q.ID)},
            //            }
            //        }
            //     }
            //};

            //try
            //{
            //    if (t.Chat is null)
            //    {
            //        await t.EditMessageAsync(
            //            text: "✅ Image selected as input for /img2img",
            //            id: query.msg_id,
            //            replyInlineMarkup: inlineKeyboardButtons
            //        );
            //    }
            //}
            //catch (Exception ex)
            //{
            //    FoxLog.WriteLine(ex.Message);
            //}
        }
    }
}
