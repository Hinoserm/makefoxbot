using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
            }
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

                // Use 'amount' and 'days' as needed


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

            long info_id = 0;

            if (argument is null || argument.Length <= 0 || !long.TryParse(argument, out info_id))
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

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0);

            if (q is not null && q.Telegram?.User.ID == t.User.ID)
            {
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
                        }
                    }
                };

                System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
                System.TimeSpan GPUTime = await q.GetGPUTime();

                await t.EditMessageAsync(
                    text: $"🖤Prompt: {q.Settings.prompt}\r\n" +
                          $"🐊Negative: {q.Settings.negative_prompt}\r\n" +
                          $"🖥️ Size: {q.Settings.width}x{q.Settings.height}\r\n" +
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

            long info_id = 0;

            if (argument is null || argument.Length <= 0 || !long.TryParse(argument, out info_id))
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

            await t.SendCallbackAnswer(query.query_id, 0, "Transferring, please wait...");

            if (q is not null && q.Telegram?.User.ID == t.User.ID)
            {
                var img = await q.GetOutputImage();

                if (img is null)
                    throw new Exception("Unable to locate image");

                var inputImage = await FoxTelegram.Client.UploadFileAsync(new MemoryStream(img.Image), "image.png");

                var msg = await FoxTelegram.Client.SendMessageAsync(t.Peer, "", new InputMediaUploadedDocument(inputImage, "image/png"));

                //await img.SaveFullTelegramFileIds(message.Document.FileId, message.Document.FileUniqueId);

            }
        }

        private static async Task CallbackCmdSelect(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            long info_id = 0;

            if (argument is null || argument.Length <= 0 || !long.TryParse(argument, out info_id))
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

            await FoxTelegram.Client.Messages_SetBotCallbackAnswer(query.query_id, 0, "Selected for img2img");

            if (q is not null && q.Telegram?.User.ID == t.User.ID)
            {
                var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                if (q.OutputImageID is null)
                    return;

                settings.selected_image = (ulong)q.OutputImageID;

                await settings.Save();

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
                                new TL.KeyboardButtonCallback { text = "Show Details", data = System.Text.Encoding.ASCII.GetBytes("/info " + q.ID)},
                            }
                        }
                     }
                };

                try
                {

                    await t.EditMessageAsync(
                        text: "✅ Image selected as input for /img2img",
                        id: query.msg_id,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine(ex.Message);
                }

            }
        }
    }
}
