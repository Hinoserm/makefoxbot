﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Management;
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
            }
        }

        private static async Task CallbackCmdModel(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            long info_id = 0;

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

            await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0);

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
                    argument = "indigoFurryMix_v105Hybrid"; //Current default


                if (await FoxWorker.GetWorkersForModel(argument) is null)
                {
                    await t.EditMessageAsync(
                        text: "❌ There are no workers currently available that can handle that model.  Please try again later.",
                        id: query.msg_id
                    );
                }
                else
                {
                    var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                    settings.model = argument;

                    settings.Save();

                    await t.EditMessageAsync(
                            text: "✅ Model selected: " + argument,
                            id: query.msg_id
                        );
                }


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

                await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0);

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
                    await t.botClient.Messages_SendMedia(
                        media: inputInvoice,
                        peer: t.Peer,
                        message: "bob",
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

            await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0);

            if (q is not null && q.telegramUserId == t.User.ID)
            {
                // Construct the inline keyboard buttons and rows
                var inlineKeyboardButtons = new ReplyInlineMarkup()
                {
                    rows = new TL.KeyboardButtonRow[]
                    {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.id) },
                                new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.id) },
                                new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.id)},
                                new TL.KeyboardButtonCallback { text = "🎨", data = System.Text.Encoding.ASCII.GetBytes("/select " + q.id)},
                            }
                        }
                    }
                };

                System.TimeSpan diffResult = DateTime.Now.Subtract(q.creation_time);
                System.TimeSpan GPUTime = await q.GetGPUTime();

                await t.EditMessageAsync(
                    text: $"🖤Prompt: {q.settings.prompt}\r\n" +
                          $"🐊Negative: {q.settings.negative_prompt}\r\n" +
                          $"🖥️ Size: {q.settings.width}x{q.settings.height}\r\n" +
                          $"🪜Sampler Steps: {q.settings.steps}\r\n" +
                          $"🧑‍🎨CFG Scale: {q.settings.cfgscale}\r\n" +
                          $"👂Denoising Strength: {q.settings.denoising_strength}\r\n" +
                          $"🧠Model: {q.settings.model}\r\n" +
                          $"🌱Seed: {q.settings.seed}\r\n" +
                          (q.worker_id is not null ? $"👷Worker: " + (await FoxWorker.GetWorkerName(q.worker_id) ?? "(unknown)") + "\r\n" : "") +
                          $"⏳Render Time: {GPUTime.ToPrettyFormat()}\r\n",
                    id: query.msg_id,
                    replyInlineMarkup: inlineKeyboardButtons
                );
            }
        }

        private static async Task CallbackCmdHelp(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            int help_id = 1;

            await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0);

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

            await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0, "Transferring, please wait...");

            if (q is not null && q.telegramUserId == t.User.ID)
            {
                var img = await q.LoadOutputImage();

                if (img is null)
                    throw new Exception("Unable to locate image");

                var inputImage = await t.botClient.UploadFileAsync(new MemoryStream(img.Image), "image.png");

                var msg = await t.botClient.SendMessageAsync(t.Peer, "", new InputMediaUploadedDocument(inputImage, "image/png"));

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

            await t.botClient.Messages_SetBotCallbackAnswer(query.query_id, 0, "Selected for img2img");

            if (q is not null && q.telegramUserId == t.User.ID)
            {
                var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                if (q.image_id is null)
                    return;

                settings.selected_image = (ulong)q.image_id;

                await settings.Save();

                var inlineKeyboardButtons = new ReplyInlineMarkup()
                {
                    rows = new TL.KeyboardButtonRow[]
                    {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.id) },
                                new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.id) },
                                new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.id)},
                                new TL.KeyboardButtonCallback { text = "🎨", data = System.Text.Encoding.ASCII.GetBytes("/select " + q.id)},
                            }
                        },
                        new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "Show Details", data = System.Text.Encoding.ASCII.GetBytes("/info " + q.id)},
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