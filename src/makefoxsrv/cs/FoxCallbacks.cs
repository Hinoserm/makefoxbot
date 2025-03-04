using MySqlConnector;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
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

            var argument = (a.Count() > 1 ? a[1] : "");

            FoxContextManager.Current = new FoxContext
            {
                Command = data,
                Message = query,
                Argument = argument,
                Telegram = t
            };

            var fUser = await FoxUser.GetByTelegramUser(t.User, false);

            FoxContextManager.Current.User = fUser;

            FoxLog.WriteLine($"Callback: {t.User}" + (t.Chat is not null ? $" in {t.Chat}" : "") + $"> {data}");

            if (fUser is null)
            {
                await t.SendCallbackAnswer(query.query_id, 0, "❌ Callback from unknown user!");

                return;
            }

            if (fUser.GetAccessLevel() == AccessLevel.BANNED)
            {
                // Banned users can't do anything.

                await t.SendCallbackAnswer(query.query_id, 0, "❌ You are banned from using this bot.");

                return;
            }

            try
            {
                await fUser.LockAsync();
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
                    case "/sampler":
                        await CallbackCmdSampler(t, query, fUser, argument);
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
                    case "/recycle":
                        await CallbackCmdRecycle(t, query, fUser, argument);
                        break;
                    case "/history":
                        await CallbackCmdHistory(t, query, fUser, argument);
                        break;
                    case "/cancel":
                        await CallbackCmdCancel(t, query, fUser, argument);
                        break;
                    case "/continue":
                        await CallbackCmdContinue(t, query, fUser, argument);
                        break;
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);

                await t.SendMessageAsync(
                    text: $"❌ Error: {ex.Message}",
                    replyToMessageId: query.msg_id
                );
            }
            finally
            {
                fUser.Unlock();
                FoxContextManager.Clear();
            }
        }

        private static async Task CallbackCmdContinue(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out ulong info_id))
            {
                throw new Exception("Malformed request");
            }

            var q = await FoxQueue.Get(info_id);
            if (q is null)
                throw new Exception("Unable to locate item");

            if (q.User is null)
                throw new Exception("Invalid user object");

            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return; // Just silently return.
            }

            await t.SendCallbackAnswer(query.query_id, 0);

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, new Message() { id = query.msg_id });

                return; // User must agree to the terms before they can use this command.
            }

            var inlineKeyboardButtons = new ReplyInlineMarkup()
            {
                rows = new TL.KeyboardButtonRow[] {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes($"/cancel {q.ID}") },
                            }
                        }
                    }
            };

            (int position, int totalItems) = FoxQueue.GetNextPosition(q.User);

            var waitMsg = await t.EditMessageAsync(
                text: $"⏳ Adding to queue ({position} of {totalItems})...",
                id: query.msg_id,
                replyInlineMarkup: inlineKeyboardButtons
            );

            await q.SetStatus(FoxQueue.QueueStatus.PENDING, query.msg_id);
            FoxQueue.Enqueue(q);
        }

        private static async Task CallbackCmdHistory(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            try
            {
                await FoxMessages.SendHistory(t, user, argument, query.msg_id, true);
                await t.SendCallbackAnswer(query.query_id, 4);

            } catch (Exception ex) {
                await t.SendCallbackAnswer(query.query_id, 0, "Error: " + ex.Message);

                FoxLog.WriteLine($"Error in CallbackCmdHistory: {ex.Message}\r\n{ex.StackTrace}");
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

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return; // Just silently return.
            }

            await t.SendCallbackAnswer(query.query_id, 0);
            
            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, new Message() { id = query.msg_id });

                return; // User must agree to the terms before they can use this command.
            }

            if (user.GetAccessLevel() < AccessLevel.ADMIN)
            {

                if (q.Settings.Width >= 1920 || q.Settings.Height >= 1920)
                {
                    await t.SendMessageAsync(
                        text: $"❌ This image is already at the maximum allowed resolution!  Enhancing again won't accomplish anything.  Please go back and enhance the original image if you'd like a different result.",
                        replyToMessageId: query.msg_id
                        );

                    return;
                }

                if (!isPremium)
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
                                    text: $"❌ Basic users are limited to 1 enhanced image per 30 minutes.\nTry again after {span.ToPrettyFormat()}.\n\nPlease consider a membership to remove this limit: /membership",
                                    replyToMessageId: query.msg_id
                                );

                                return;
                            }
                        }
                    }
                }
            }

            FoxUserSettings settings = q.Settings.Copy();

            if (true || q.Type == FoxQueue.QueueType.IMG2IMG)
            {
                if (q.OutputImageID is null)
                    throw new Exception("Missing output image ID.");

                //(settings.width, settings.height) = FoxImage.CalculateLimitedDimensions(settings.width * 2, settings.height * 2, 1920);

                settings.Seed = -1;
                settings.hires_denoising_strength = 0.33M;
                settings.hires_steps = 15;
                settings.hires_enabled = true;

                settings.SelectedImage = q.OutputImageID.Value;

                uint width = Math.Max(settings.Width, settings.hires_width);
                uint height = Math.Max(settings.Height, settings.hires_height);

                (settings.hires_width, settings.hires_height) = FoxImage.CalculateLimitedDimensions(width * 2, height * 2, 1920);
            }
            //else if (q.Type == FoxQueue.QueueType.TXT2IMG)
            //{
            //    settings.hires_denoising_strength = 0.45M;
            //    settings.hires_steps = 15;
            //    settings.hires_enabled = true;

            //    uint width = Math.Max(settings.Width, settings.hires_width);
            //    uint height = Math.Max(settings.Height, settings.hires_height);

            //    (settings.hires_width, settings.hires_height) = FoxImage.CalculateLimitedDimensions(width * 2, height * 2, 1920);
            //}
            //else
            //    throw new Exception("Invalid queue type");

            settings.regionalPrompting = q.RegionalPrompting; //Have to copy this over manually

            await FoxGenerate.Generate(t, settings, new TL.Message() { id = query.msg_id }, user, FoxQueue.QueueType.IMG2IMG, true, q);
        }

        private static async Task CallbackCmdRecycle(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {
            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out ulong info_id))
            {
                throw new Exception("Malformed request");
            }

            var q = await FoxQueue.Get(info_id);
            if (q is null)
                throw new Exception("Unable to locate queue item");

            // Treat enhanced images as fresh, new images for the sake of variations.
            if (q.Settings.variation_seed is not null && !q.Enhanced)
            {
                if (q.OriginalID is null)
                    throw new Exception("Missing original ID for variation request");

                q = await FoxQueue.Get(q.OriginalID.Value);

                if (q is null)
                    throw new Exception("Unable to load original request data");
            }

            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return; // Just silently return.
            }

            await t.SendCallbackAnswer(query.query_id, 0);

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, new Message() { id = query.msg_id });

                return; // User must agree to the terms before they can use this command.
            }

            if (!isPremium)
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "SELECT COUNT(*) FROM queue WHERE uid = @uid AND status != 'CANCELLED' AND original_id = @original_id AND variation_seed IS NOT NULL AND date_finished > @now - INTERVAL 3 HOUR";
                        cmd.Parameters.AddWithValue("uid", user.UID);
                        cmd.Parameters.AddWithValue("original_id", q.ID);
                        cmd.Parameters.AddWithValue("now", DateTime.Now);
                        await using var reader = await cmd.ExecuteReaderAsync();

                        if (reader.HasRows && await reader.ReadAsync())
                        {
                            var count = reader.GetInt32(0);
                            var variation_limit = FoxSettings.Get<int?>("VariationFreeLimit") ?? 1;

                            if (count >= variation_limit)
                            {
                                var plural = variation_limit == 1 ? "" : "s";

                                await t.SendMessageAsync(
                                    text: $"❌ Basic users are limited to {variation_limit} variation{plural} per image.\n\nPlease consider a membership to remove this limit: /membership",
                                    replyToMessageId: query.msg_id
                                );

                                return;
                            }
                        }
                    }
                }
            }

            FoxUserSettings settings = q.Settings.Copy();

            settings.variation_seed = FoxQueue.GetRandomInt32();

            if (q.Type == FoxQueue.QueueType.IMG2IMG)
            {
                settings.variation_strength = 0.3M; //This seems to need a significant boost to make a difference w/IMG2IMG
            }
            else
            {
                settings.variation_strength = 0.02M;
            }

            await FoxGenerate.Generate(t, settings, new TL.Message() { id = query.msg_id }, user, q.Type, q.Enhanced, q);
        }

        private static async Task CallbackCmdLanguage(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            if (argument is null || argument.Length <= 0)
            {
                argument = "en";
            }

            await user.SetPreferredLanguage(argument);

            await t.SendCallbackAnswer(query.query_id, 0, user.Strings.Get("Lang.AnswerCallbackMsg"));

            await FoxMessages.SendWelcome(t, user, null, query.msg_id);

        }

        private static async Task CallbackCmdTerms(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            if (argument is null || argument.Length <= 0)
            {
                argument = "en";
            }

            await user.SetTermsAccepted();

            await t.SendCallbackAnswer(query.query_id, 0, user.Strings.Get("Terms.AgreeClicked"));

            await FoxMessages.SendTerms(t, user, null, query.msg_id);

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

            await t.SendCallbackAnswer(query.query_id, 0);

            if (argument == "cancel")
            {
                await t.EditMessageAsync(
                        text: "✅ Operation cancelled.",
                        id: query.msg_id
                    );

                return;
            }

            if (argument == "more")
            {
                await FoxMessages.SendModelList(t, user, null, 2, query.msg_id);

                return;
            }

            if (argument == "back")
            {
                await FoxMessages.SendModelList(t, user, null, 1, query.msg_id);

                return;
            }

            if (argument == "default")
                argument = null;

            var model = FoxModel.GetModelByName(argument ?? FoxSettings.Get<string>("DefaultModel")!);

            if (model is null)
            {
                await t.EditMessageAsync(
                    text: "❌ Unknown model selected.",
                    id: query.msg_id
                );

                return;
            } else if (model.GetWorkersRunningModel().Count < 1) {
                await t.EditMessageAsync(
                    text: "❌ There are no workers currently available that can handle that model.  Please try again later.",
                    id: query.msg_id
                );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.Model = model.Name;

            await settings.Save();

            await model.LoadModelMetadataFromDatabase(); // Refresh info in case it changed.

            StringBuilder message = new StringBuilder();

            message.AppendLine("✅ <b>Model selected:</b> " + settings.Model);

            if (model.Description is not null)
            {
                message.AppendLine();
                message.AppendLine("📝 <b>Description:</b> " + model.Description);
            }

            if (model.Notes is not null)
            {
                message.AppendLine();
                message.AppendLine("⚠️ <b>Important Notes:</b> " + model.Notes);
            }

            if (model.InfoUrl is not null)
            {
                message.AppendLine();
                message.AppendLine("🔗 <a href=\"" + model.InfoUrl + "\">More Information</a>");
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (model.IsPremium && !isPremium)
            {
                message.AppendLine();
                message.AppendLine("(🔒 This is a premium model and may require a membership to use)");
            }

            var msg = message.ToString();
            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            await t.EditMessageAsync(
                    text: msg,
                    entities: entities,
                    disableWebPagePreview: true,
                    id: query.msg_id
                );
        }

        private static async Task CallbackCmdSampler(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
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

            if (argument == "premium")
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only premium members can select this option!");
                return;
            }

            await t.SendCallbackAnswer(query.query_id, 0);

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
                    argument = FoxSettings.Get<string>("DefaultSampler")!;
                }

                var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                settings.Sampler = argument;

                await settings.Save();

                await t.EditMessageAsync(
                        text: "✅ Sampler selected: " + settings.Sampler,
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

            await t.SendCallbackAnswer(query.query_id, 0);

            switch (argument)
            {
                case "cancel":
                    await t.EditMessageAsync(
                            text: "✅ Operation cancelled.",
                            id: query.msg_id
                        );
                    break;
                case "remove":
                    await t.DeleteMessage(query.msg_id);
 
                    break;
                case "custom":
                    await t.EditMessageAsync(
                            text: "🚧 Not Yet Implemented.",
                            id: query.msg_id
                        );
                    break;

                case "stars":

                    List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

                    // Add "Stars" button
                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonBuy[]
                        {
                            new() { text = "⭐ 800 Stars (30 Days)",  }
                        }
                    });

                    // Add cancel button on its own row at the end
                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new() { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/donate remove") }
                        }
                    });

                    var prices = new TL.LabeledPrice[] {
                        new TL.LabeledPrice { label = $"30 Day Membership", amount = 800 },
                    };

                    var inputInvoice = new TL.InputMediaInvoice
                    {
                        title = $"One-Time Payment for User ID {user.UID}",
                        description = $"MakeFox Membership",
                        payload = System.Text.Encoding.UTF8.GetBytes($"PAY_{user.UID}_STARS"),
                        provider_data = new TL.DataJSON { data = "{}" },
                        invoice = new TL.Invoice
                        {
                            currency = "XTR",
                            prices = prices,
                            flags = TL.Invoice.Flags.test
                        }
                    };

                    var inlineKeyboard = new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() };

                    var sentMessage = await t.SendMessageAsync(
                        media: inputInvoice,
                        text: "30 Days Membership",
                        replyInlineMarkup: inlineKeyboard,
                        disableWebPagePreview: true
                    );

                    break;
                default: 
                    throw new Exception("Unsupported Payment Request");
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



            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator can click this button!");
            }
            else 
            {
                await t.SendCallbackAnswer(query.query_id, 0);
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
                                new TL.KeyboardButtonCallback { text = "🎲", data = System.Text.Encoding.ASCII.GetBytes("/recycle " + q.ID) },
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

                var sb = new StringBuilder();

                System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
                System.TimeSpan GPUTime = await q.GetGPUTime();

                uint width = Math.Max(q.Settings.Width, q.Settings.hires_width);
                uint height = Math.Max(q.Settings.Height, q.Settings.hires_height);

                var sizeString = $"{width}x{height}" + (q.Settings.hires_enabled ? $" (upscaled from {q.Settings.Width}x{q.Settings.Height})" : "");

                //if (q.Settings.UpscalerWidth is not null && q.Settings.UpscalerHeight is not null)
                //    sizeString += $" (upscaled from {q.Settings.width}x{q.Settings.height})";

                // Build the main message
                sb.AppendLine($"🖤Prompt: {q.Settings.Prompt}");
                sb.AppendLine($"🐊Negative: {q.Settings.NegativePrompt}");
                sb.AppendLine($"🖥️ Size: {sizeString}");
                sb.AppendLine($"🪜Sampler: {q.Settings.Sampler} ({q.Settings.steps} steps)");
                sb.AppendLine($"🧑‍🎨CFG Scale: {q.Settings.CFGScale}");
                if (q.Type == FoxQueue.QueueType.IMG2IMG)
                    sb.AppendLine($"👂Denoising Strength: {q.Settings.DenoisingStrength}");
                sb.AppendLine($"🧠Model: {q.Settings.Model}");

                if (q.Settings.variation_seed is not null && q.Settings.variation_strength is not null)
                {
                    var variation_percent = (int)(q.Settings.variation_strength * 100);

                    sb.AppendLine($"🌱Seed: {q.Settings.Seed} ({q.Settings.variation_seed}@{variation_percent}%)");
                }
                else
                    sb.AppendLine($"🌱Seed: {q.Settings.Seed}");

                if (q.WorkerID is not null)
                {
                    string workerName = await FoxWorker.GetWorkerName(q.WorkerID);
                    sb.AppendLine($"👷Worker: {workerName}");
                }

                sb.AppendLine($"⏳Render Time: {GPUTime.ToPrettyFormat()}");

                try
                {
                    await t.EditMessageAsync(
                        text: sb.ToString(),
                        id: query.msg_id,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                catch (WTException ex) when (ex.Message == "MEDIA_CAPTION_TOO_LONG" || ex.Message == "MESSAGE_ID_INVALID") 
                {
                    var newmsg = await t.SendMessageAsync(
                        text: sb.ToString(),
                        replyToMessageId: q.MessageID,
                        replyInlineMarkup: inlineKeyboardButtons
                    );

                    FoxLog.WriteLine($"CallbackCmdInfo FAIL: {query.msg_id} ({q.ID}) NEW ID {newmsg.ID} Reason: {ex.Message}", LogLevel.DEBUG);
                }
            }
        }

        private static async Task CallbackCmdCancel(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            ulong info_id = 0;

            FoxQueue? q = null;

            if (argument is null || argument.Length <= 0 || !ulong.TryParse(argument, out info_id))
            {
                // There are a few cases where we don't know the queue ID prior to the keyboard button being built, so in
                //  those cases we send the cancel callback without a parameter and hope we can find it with the message ID.

                q = await FoxQueue.GetByMessage(t, query.msg_id);
            }
            else
            {
                q = await FoxQueue.Get(info_id);
            }

            if (q is null)
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Unable to locate request or unauthorized user.");
                return;
            }

            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator can click this button!");
                return;
            }

            await q.Cancel();
        }

        private static async Task CallbackCmdHelp(FoxTelegram t, UpdateBotCallbackQuery query, FoxUser user, string? argument = null)
        {

            int help_id = 1;

            await t.SendCallbackAnswer(query.query_id, 0);

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
                replyInlineMarkup: inlineKeyboardButtons,
                replyToMessageId: query.msg_id
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
                await t.SendCallbackAnswer(query.query_id, 0, "Error loading image.");
                return;
            }

            if (q.Telegram?.User.ID != t.User.ID && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendCallbackAnswer(query.query_id, 0, "Only the original creator may click this button!");
                return;
            }

            await t.SendCallbackAnswer(query.query_id, 0, "Transferring, please wait...");

            var img = await q.GetOutputImage();

            if (img is null || img.Image is null)
                throw new Exception("Unable to locate image");

            var imageData = new MemoryStream(img.Image);

            //bool addWatermark = !(q.User.CheckAccessLevel(AccessLevel.PREMIUM));

            //if (addWatermark)
            //{
            //    var outputStream = new MemoryStream();

            //    using Image<Rgba32> image = Image.Load<Rgba32>(new MemoryStream(img.Image));
                
            //    using var outputImage = FoxWatermark.ApplyWatermark(image);

            //    outputImage.SaveAsPng(outputStream, new PngEncoder());
            //    outputStream.Position = 0;

            //    imageData =  outputStream;
            //}
            
            var inputImage = await FoxTelegram.Client.UploadFileAsync(imageData, $"{FoxTelegram.Client.User.username}_full_image_{q.ID}.png");

            await t.SendMessageAsync(
                               replyToMessageId: q.MessageID,
                               replyToTopicId: q.ReplyTopicID ?? 0,
                               media: new InputMediaUploadedDocument(inputImage, "image/png")
            );

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
                await t.SendCallbackAnswer(query.query_id, 0, "Error loading image.");
                return;
            }

            await t.SendCallbackAnswer(query.query_id, 0, "Selected for img2img");

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (q.OutputImageID is null)
                return;

            settings.SelectedImage = (ulong)q.OutputImageID;

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
