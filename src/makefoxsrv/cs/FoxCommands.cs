using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Reflection.Metadata;
using System.Linq.Expressions;
using MySqlConnector;
using WTelegram;
using TL;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.ComponentModel.Design;
using Stripe;
using Stripe.FinancialConnections;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using System.Text.RegularExpressions;

namespace makefoxsrv
{
    internal class FoxCommandHandler
    {
        private static readonly Dictionary<string, Func<FoxTelegram, TL.Message, FoxUser, String?, Task>> CommandMap = new Dictionary<string, Func<FoxTelegram, TL.Message, FoxUser, String?, Task>>
        {
            { "/pizza",       CmdTest },
            { "/test",        CmdTest },
            //--------------- -----------------
            { "/img2img",     CmdImg2Img  },
            //--------------- -----------------
            { "/generate",    CmdGenerate },
            //--------------- -----------------
            { "/setnegative", CmdSetNegative },
            { "/negative",    CmdSetNegative },
            //--------------- -----------------
            { "/setprompt",   CmdSetPrompt },
            { "/prompt",      CmdSetPrompt },
            //--------------- -----------------
            { "/setsteps",    CmdSetSteps },
            { "/steps",       CmdSetSteps },
            //--------------- -----------------
            { "/setscale",    CmdSetCFG },
            { "/setcfg",      CmdSetCFG },
            { "/cfg",         CmdSetCFG },
            //--------------- -----------------
            { "/setdenoise",  CmdSetDenoise },
            { "/denoise",     CmdSetDenoise },
            { "/noise",       CmdSetDenoise },
            //--------------- -----------------
            { "/setsize",     CmdSetSize },
            { "/size",        CmdSetSize },
            //--------------- -----------------
            { "/current",     CmdCurrent },
            { "/select",      CmdSelect },
            //--------------- -----------------
            { "/start",       CmdStart },
            //--------------- -----------------
            { "/help",        CmdHelp },
            //--------------- -----------------
            { "/commands",    CmdCmdList },
            { "/cmdlist",     CmdCmdList },
            //--------------- -----------------
            { "/seed",        CmdSetSeed },
            { "/setseed",     CmdSetSeed },
            //--------------- -----------------
            { "/model",       CmdModel },
            { "/sampler",     CmdSampler },
            //--------------- -----------------
            { "/cancel",      CmdCancel },
            //--------------- -----------------
            { "/donate",      CmdDonate },
            { "/membership",  CmdDonate },
            //--------------- -----------------
            { "/ban",         CmdBan },
            { "/broadcast",   CmdBroadcast },
            //--------------- -----------------
            { "/info",        CmdInfo },
            { "/privacy",     CmdPrivacy },
            { "/history",     CmdHistory },
            { "/admin",       CmdAdmin },
            //--------------- -----------------
            { "/styles",      CmdStyles },
            //--------------- -----------------
            { "/stickerify",  CmdStickerify }
        };

        public static async Task HandleCommand(FoxTelegram t, Message message)
        {
            var explicitlyNamed = (t.Chat is null);

            if (message is null)
                throw new ArgumentNullException();

            if (FoxTelegram.Client is null)
                throw new Exception("FoxTelegram.Client is null");

            if (message.message is null || message.message.Length < 2)
                return;

            var args = message.message.Split([' ', '\n'], 2);

            if (message.message[0] != '/')
            {
                if (t.Chat is null && !string.IsNullOrEmpty(FoxMain.settings?.llmApiKey))
                {
                    var llmUser = await FoxUser.GetByTelegramUser(t.User, false);

                    if (llmUser is not null)
                    {
                        FoxContextManager.Current.User = llmUser;
                        await FoxLLM.ProcessLLMRequest(t, llmUser, message); // Send to LLM
                    }
                }
                return; // Not a command, skip it.
            }

            var command = args[0];

            var c = command.Split('@', 2);
            if (c.Count() == 2)
            {
                if (c[1].ToLower() != FoxTelegram.Client.User.username.ToLower())
                    return; // Not for us, skip it.

                explicitlyNamed = true;
                command = c[0];
            }

            var argument = (args.Count() >= 2 ? args[1].TrimStart() : null);

            // Initialize FoxContext for this command
            FoxContextManager.Current = new FoxContext
            {
                Message = message,
                Command = command,
                Argument = argument,
                Telegram = t
            };

            var commandHandler = FindBestMatch(command);

            //FoxLog.WriteLine($"{message.ID}: Found command for {t.User.username}: {commandHandler.Method.Name}");

            if (commandHandler is not null)
            {
                var fUser = await FoxUser.GetByTelegramUser(t.User, true);

                FoxContextManager.Current.User = fUser;

                //FoxLog.WriteLine($"{message.ID}: Found UID for {t.User}: {fUser?.UID}");

                if (fUser is null)
                {
                    fUser = await FoxUser.CreateFromTelegramUser(t.User);

                    if (fUser is null)
                        throw new Exception("Unable to create new user");
                }
                else
                    await fUser.UpdateTimestamps();

                FoxContextManager.Current.User = fUser;

                if (fUser.GetAccessLevel() == AccessLevel.BANNED)
                {
                    await t.SendMessageAsync(
                        text: "❌ You are banned from using this bot.",
                            replyToMessage: message
                        );

                    return;
                }

                if (t.Chat is not null && !await FoxGroupAdmin.CheckGroupTopicAllowed(t.Chat, t.User, message.ReplyHeader?.TopicID ?? 0))
                {
                    await t.SendMessageAsync(
                        text: "❌ Commands are not permitted in this topic.",
                            replyToMessage: message
                        );

                    return;
                }

                //FoxLog.WriteLine($"{message.ID}: Running command for UID {fUser?.UID}...");
                try
                {
                    await fUser.LockAsync();
                    await commandHandler(t, message, fUser, argument);
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);

                    await t.SendMessageAsync(
                        text: $"❌ Error: {ex.Message}",
                        replyToMessage: message
                    );
                }
                finally
                {
                    fUser.Unlock();
                    FoxContextManager.Clear();
                }
                //FoxLog.WriteLine($"{message.ID}: Finished running command for {fUser?.UID}.");
            }
            else if (explicitlyNamed) //Only send this message if we were explicitly named in the chat (e.g. /command@botname)
            {

                await t.SendMessageAsync(
                    text: $"🦊 I'm sorry, I didn't understand that command.  Try /help.",
                    replyToMessage: message
                );
            }
        }
        public static string GetCommandDescription(Func<FoxTelegram, Message, FoxUser, String?, Task> commandFunction)
        {
            var methodInfo = commandFunction.Method;
            var attribute = methodInfo.GetCustomAttribute<CommandDescriptionAttribute>();
            return attribute?.Description ?? "";
        }

        public static string GetCommandArguments(Func<FoxTelegram, Message, FoxUser, String?, Task> commandFunction)
        {
            var methodInfo = commandFunction.Method;
            var attribute = methodInfo.GetCustomAttribute<CommandArgumentsAttribute>();
            return attribute?.Arguments ?? "";
        }

        public static string GetCommandDescription(string commandName)
        {
            var methodInfo = typeof(FoxCommandHandler).GetMethod(commandName, BindingFlags.NonPublic | BindingFlags.Static);
            if (methodInfo != null)
            {
                var attribute = methodInfo.GetCustomAttribute<CommandDescriptionAttribute>();
                return attribute?.Description ?? "";
            }
            return "";
        }

        private static Func<FoxTelegram, Message, FoxUser, string?, Task>? FindBestMatch(string command)
        {
            // Ensure CommandMap and command are handled as non-null
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            List<string> potentialMatches = new();

            foreach (var cmd in CommandMap.Keys)
            {
                if (cmd.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                {
                    potentialMatches.Add(cmd);
                }
            }

            if (potentialMatches.Count == 0)
            {
                // No matches found
                return null;
            }

            // Check for an exact match first
            if (CommandMap.TryGetValue(command, out var exactMatch))
            {
                return exactMatch;
            }

            // Find the shortest command in potential matches
            string? shortestMatch = potentialMatches
                .OrderBy(s => s.Length)
                .FirstOrDefault();

            if (shortestMatch == null)
            {
                // No valid shortest match
                return null;
            }

            // Check if the command is a unique prefix
            bool isUniquePrefix = potentialMatches.All(s => !s.StartsWith(command, StringComparison.OrdinalIgnoreCase) || s.Equals(shortestMatch, StringComparison.OrdinalIgnoreCase));

            if (isUniquePrefix && CommandMap.TryGetValue(shortestMatch, out var uniqueMatch))
            {
                // Return the command if it's a unique prefix
                return uniqueMatch;
            }

            // Ambiguous command
            return null;
        }

        public static async Task SetBotCommands(Client client)
        {
            var commandList = CommandMap
                .GroupBy(pair => pair.Value, pair => pair.Key.TrimStart('/'))
                .Select(group => new
                {
                    Command = group.OrderByDescending(cmd => cmd.Length).First(),
                    Description = GetCommandDescription(group.Key)
                })
                .Where(cmd => !string.IsNullOrWhiteSpace(cmd.Description))
                .OrderBy(cmd => cmd.Command) // Order commands alphabetically
                .Select(cmd => new TL.BotCommand { command = cmd.Command, description = cmd.Description })
                .ToArray();

            await client.Bots_SetBotCommands(new TL.BotCommandScopeUsers(), null, commandList);
        }

        [AttributeUsage(AttributeTargets.Method, Inherited = false)]
        public class CommandDescriptionAttribute : Attribute
        {
            public string Description { get; }

            public CommandDescriptionAttribute(string description)
            {
                Description = description;
            }
        }

        public class CommandArgumentsAttribute : Attribute
        {
            public string Arguments { get; }

            public CommandArgumentsAttribute(string arguments)
            {
                Arguments = arguments;
            }
        }

        //[CommandDescription("Load the styles menu")]
        //[CommandArguments("")]
        private static async Task CmdStyles(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            // Initialize a list to hold TL.KeyboardButtonRow for each row of buttons
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            // List to accumulate buttons for the current row
            List<TL.KeyboardButtonWebView> currentRowButtons = new List<TL.KeyboardButtonWebView>();


            string webUrl = $"{FoxMain.settings!.WebRootUrl}tgapp/styles.php";

            currentRowButtons.Add(new TL.KeyboardButtonWebView { text = "Edit Styles", url = webUrl });

            buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() };

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("<b>Styles Menu</b>\n"); //Intentional extra newline.
            sb.AppendLine("Styles allow you to have groups of extra text that are automatically added to your prompts and can be easily toggled on and off.\n");
            sb.AppendLine("When a style is enabled (✅), it will be automatically appended to your prompts whenever you generate an image.\n");
            sb.AppendLine("<u>Telegram can only display your first few styles below</u>; click the <b>Edit Styles</b> button to access your full list, add new styles, or make changes.\n");

            var msg = sb.ToString();

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            var sentMessage = await t.SendMessageAsync(
                text: msg,
                replyInlineMarkup: inlineKeyboard,
                entities: entities,
                disableWebPagePreview: true,
                replyToMessage: message
            );
        }

        private static async Task CmdAdmin(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessage: message
                );

                return;
            }

            var args = argument?.Split(' ', 2);

            string command = args is not null ? args[0] : "";
            string? commandArgs = args?.Length > 1 ? args[1] : null;

            switch (command.ToLower())
            {
                case "#leave":
                    await FoxAdmin.HandleLeaveGroup(t, message, commandArgs);
                    break;
                case "#uncache":
                    await FoxAdmin.HandleUncache(t, message, commandArgs);
                    break;

                case "#ban":
                    await FoxAdmin.HandleBan(t, message, commandArgs);
                    break;

                case "#unban":
                    await FoxAdmin.HandleUnban(t, message, commandArgs);
                    break;
                case "#resetterms":
                case "#resettos":
                    await FoxAdmin.HandleResetTerms(t, message, commandArgs);
                    break;
                case "#premium":
                    await FoxAdmin.HandlePremium(t, message, commandArgs);
                    break;
                case "#showgroups":
                case "#groups":
                    await FoxAdmin.HandleShowGroups(t, message, commandArgs);
                    break;
                case "#payments":
                case "#pay":
                    await FoxAdmin.HandleShowPayments(t, message, commandArgs);
                    break;
                case "#rotate":
                    await FoxLog.LogRotator.Rotate();
                    break;
                case "#forward":
                    await FoxAdmin.HandleForward(t, message, commandArgs);
                    break;

                default:
                    await t.SendMessageAsync(
                        text: "❌ Unknown command.  Use one of these:\r\n  #uncache, #ban, #unban, #resetterms, #resettos",
                        replyToMessage: message
                    );
                    break;
            }
        }

        [CommandDescription("Show your recent history")]
        [CommandArguments("")]
        private static async Task CmdHistory(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxMessages.SendHistory(t, user, argument, message.ID);
        }


        [CommandDescription("Purchase a membership")]
        [CommandArguments("")]
        private static async Task CmdDonate(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            if (string.IsNullOrEmpty(FoxMain.settings?.TelegramPaymentToken))
                throw new Exception("Payments are currently disabled. (token not set)");

            // Define donation amounts in whole dollars
            int[] donationAmounts = new int[] { 5, 10, 20, 40, 60, 100 };

            // Initialize a list to hold TL.KeyboardButtonRow for each row of buttons
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            // List to accumulate buttons for the current row
            List<TL.KeyboardButtonWebView> currentRowButtons = new List<TL.KeyboardButtonWebView>();

            var pSession = await FoxPayments.Invoice.Create(user);

            // Loop through the donation amounts and create buttons
            for (int i = 0; i < donationAmounts.Length; i++)
            {
                int amountInCents = donationAmounts[i] * 100;
                int days = FoxPayments.CalculateRewardDays(amountInCents);
                string buttonText = $"💳 ${donationAmounts[i]} ({days} days)";

                string webUrl = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?tg=1&id={pSession.UUID}&amount={amountInCents}";

                currentRowButtons.Add(new TL.KeyboardButtonWebView { text = buttonText, url = webUrl });

                // Every two buttons or at the end, add the current row to buttonRows and start a new row
                if ((i + 1) % 2 == 0 || i == donationAmounts.Length - 1)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });
                    currentRowButtons = new List<TL.KeyboardButtonWebView>(); // Reset for the next row
                }
            }

            // Add lifetime access button
            //buttonRows.Add(new TL.KeyboardButtonRow
            //{
            //    buttons = new TL.KeyboardButtonCallback[]
            //    {
            //        new() { text = "✨💰 💳 $600 (Lifetime Access!) 💰✨", data = System.Text.Encoding.UTF8.GetBytes("/donate 600 lifetime") }
            //    }
            //});

            // Add "Custom Amount" button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonUrl[]
                {
                    new() { text = "🔗 Pay in Web Browser", url = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?id={pSession.UUID}" }
                }
            });

            // Add Stars button
            //buttonRows.Add(new TL.KeyboardButtonRow
            //{
            //    buttons = new TL.KeyboardButtonCallback[]
            //    {
            //        new() { text = "⭐ Use Telegram Stars ⭐", data = System.Text.Encoding.UTF8.GetBytes("/donate stars") }
            //    }
            //});

            // Add cancel button on its own row at the end
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/donate cancel") }
                }
            });



            var inlineKeyboard = t.Chat is not null ? null : new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() };

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("<b>MakeFox Membership – Unlock Exclusive Benefits</b>\n"); //Intentional extra newline.

            if (user.datePremiumExpires > DateTime.Now)
            {
                //User is already a premium member
                sb.AppendLine("Thank you for purchasing a MakeFox membership!\n");
                sb.AppendFormat("Your membership is active until <b>{0:MMMM d\\t\\h yyyy}</b>.\n", user.datePremiumExpires);
                sb.AppendLine("\nYou can purchase additional days, which will be added to your existing membership time.");
            }
            else
            {
                sb.AppendLine("Thank you for considering a membership. <i>MakeFox Group, Inc.</i> is a registered US non-profit, and your support is crucial for the development and maintenance of our platform.");
                if (user.datePremiumExpires < DateTime.Now)
                {
                    sb.AppendFormat("\nYour previous membership expired on <b>{0:MMMM d\\t\\h yyyy}</b>.\n", user.datePremiumExpires);
                }
            }

            sb.AppendLine("\n<b>Membership Benefits:</b>\n");
            sb.AppendLine(" - <b>High-Resolution Image Enhancements:</b> Members enjoy nearly unlimited enhancements and variations, subject to fair usage limits.\n");

            sb.AppendLine(" - <b>Flexible Image Dimensions:</b> Create images in any shape and size up to 3.7 million pixels.\n");

            sb.AppendLine(" - <b>Queue Priority:</b> Your requests get placed first in the queue, allowing for shorter wait times.\n");

            sb.AppendLine(" - <b>Early Access:</b> Be the first to try new experimental models and features.\n");

            sb.AppendLine("<a href=\"https://telegra.ph/MakeFox-Membership-06-07\"><b>Click here for more information.</b></a>\n");

            sb.Append("<i>Note: Membership purchases are not tax-deductible.</i>");

            if (t.Chat is not null)
            {
                sb.AppendLine($"\n\n<b>You cannot purchase a membership from within a group chat.\n\nTo purchase a membership, please contact @{FoxTelegram.BotUser?.MainUsername} directly.</b>");
            }
            
            var msg = sb.ToString();

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            var sentMessage = await t.SendMessageAsync(
                text: msg,
                replyInlineMarkup: inlineKeyboard,
                entities: entities,
                disableWebPagePreview: true,
                replyToMessage: message
            );
            
            pSession.TelegramMessageId = sentMessage.ID;
            pSession.TelegramPeerId = sentMessage.peer_id;
            await pSession.Save();
        }

        [CommandDescription("View the privacy policy")]
        private static async Task CmdPrivacy(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var outMsg = user.Strings.Get("Terms.PrivacyPolicy");
            var entities = FoxTelegram.Client.HtmlToEntities(ref outMsg);

            await t.SendMessageAsync(
                text: outMsg,
                entities: entities,
                replyToMessage: message
            );
        }

        [CommandDescription("It's delicious!")]
        [CommandArguments("<pants level> [<pizza>]")]
        private static async Task CmdTest(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, message, 0);

                return; // User must agree to the terms before they can use this command.
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.Prompt = "cute male fox wearing (jeans), holding a (slice of pizza), happy, smiling, (excited), energetic, sitting, solo, vibrant and surreal background, (80's theme), masterpiece, perfect anatomy, shoes";
            settings.NegativePrompt = "(female), penis, nsfw, eating, boring_e621_fluffyrock_v4, deformityv6, easynegative, bad anatomy, multi limb, multi tail, ((human)), text, signature, watermark, logo, writing, words";
            settings.CFGScale = 10M;
            settings.steps = 20;
            settings.Width = 768;
            settings.Height = 768;
            settings.Seed = -1;

            Message waitMsg = await t.SendMessageAsync(
                text: $"⏳👖🍕🦊 Please wait...",
                replyToMessage: message
            );

            try
            {
                if (waitMsg is null)
                    throw new Exception("Unable to send start message.  Giving up.");

                var q = await FoxQueue.Add(t, user, settings, FoxQueue.QueueType.TXT2IMG, waitMsg.ID, message);

                FoxContextManager.Current.Queue = q;

                FoxQueue.Enqueue(q);
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("Error: " + ex.Message);
            }
        }

        [CommandDescription("Send this in a reply to any message containing an image to select it for /img2img")]
        private static async Task CmdSelect(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var img = await FoxImage.SaveImageFromReply(t, message);

            if (img is null)
            {
                await t.SendMessageAsync(
                        text: "❌ Error: That message doesn't contain an image.  You must send this command as a reply to a message containing an image.",
                        replyToMessage: message
                        );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.SelectedImage = img.ID;

            await settings.Save();

            try
            {
                Message waitMsg = await t.SendMessageAsync(
                    text: "✅ Image saved and selected as input for /img2img",
                    replyToMessageId: (int)(img.TelegramMessageID ?? message.ID)
                );
            }
            catch
            {
                Message waitMsg = await t.SendMessageAsync(
                    text: "✅ Image saved and selected as input for /img2img"
                );
            }

        }

        private static async Task CmdStickerify(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var stickerImg = await FoxImage.SaveImageFromReply(t, message);

            if (stickerImg is null)
            {
                await t.SendMessageAsync(
                        text: "❌ Error: That message doesn't contain an image.  You must send this command as a reply to a message containing an image.",
                        replyToMessage: message
                        );

                return;
            }

            using Image<Rgba32> img = Image.Load<Rgba32>(stickerImg.Image);

            // Here we enable drop shadow with custom parameters. 
            Image<Rgba32> processed = FoxStickerify.ProcessSticker(img, tolerance: 50, extraMargin: 3, inwardEdge: 3,
                                                      addDropShadow: true,
                                                      dropShadowOffsetX: 5,
                                                      dropShadowOffsetY: 5,
                                                      dropShadowBlurSigma: 3f,
                                                      dropShadowOpacity: 0.5f);

            processed = FoxStickerify.CropAndEnsure512x512(processed);

            var outputStream = new MemoryStream();


            processed.SaveAsPng(outputStream, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });

            outputStream.Position = 0;

            var outputImage = await FoxTelegram.Client.UploadFileAsync(outputStream, $"{FoxTelegram.Client.User.username}_sticker_{stickerImg.ID}.png");


            await t.SendMessageAsync(
                               replyToMessage: message,
                               media: new InputMediaUploadedDocument(outputImage, "image/png")
            );

        }

        [CommandDescription("Show list of available commands")]
        private static async Task CmdCmdList(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var commandGroups = CommandMap
                .GroupBy(pair => pair.Value, pair => pair.Key)
                .ToDictionary(g => g.Key, g => g.ToList());

            var helpEntries = new List<string>();
            foreach (var group in commandGroups.OrderBy(g => g.Value.First()))
            {
                var command = group.Value.OrderByDescending(cmd => cmd.Length).First();
                string description = GetCommandDescription(group.Key);
                string arguments = GetCommandArguments(group.Key);

                if (!string.IsNullOrWhiteSpace(description))
                {
                    helpEntries.Add($"{command} {arguments}\n    {description}\n");
                }
            }

            await t.SendMessageAsync(
                text: string.Join(Environment.NewLine, helpEntries),
                replyToMessage: message
            );
        }

        private static async Task CmdStart(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxMessages.SendWelcome(t, user, message);
        }

        [CommandDescription("Show helpful information")]
        private static async Task CmdHelp(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            var inlineKeyboardButtons = new ReplyInlineMarkup()
            {
                rows = new TL.KeyboardButtonRow[]
                {
                    new TL.KeyboardButtonRow {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new TL.KeyboardButtonCallback { text = "More Help", data = System.Text.Encoding.ASCII.GetBytes("/help 2") },
                        }
                    }
                }
            };

            await t.SendMessageAsync(
                text: FoxStrings.text_help[0],
                replyToMessage: message,
                replyInlineMarkup: inlineKeyboardButtons
            );
        }

        [CommandDescription("Run an img2img generation.  Requires you to have previously uploaded an image.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdImg2Img(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxGenerate.HandleCmdGenerate(t, message, user, argument, FoxQueue.QueueType.IMG2IMG);
        }

        [CommandDescription("Run a standard txt2img generation.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdGenerate(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxGenerate.HandleCmdGenerate(t, message, user, argument, FoxQueue.QueueType.TXT2IMG);
        }

        [CommandDescription("Change current AI sampler.")]
        [CommandArguments("")]
        private static async Task CmdSampler(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            bool userIsPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);


            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            var cmdText = "SELECT * FROM samplers";

            MySqlCommand cmd = new MySqlCommand(cmdText, SQL);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string samplerName = reader.GetString("sampler");
                    bool isPremium = reader.GetBoolean("premium");

                    var buttonLabel = $"{samplerName}";
                    var buttonData = $"/sampler {samplerName}";

                    if (isPremium)
                    {
                        if (!userIsPremium)
                        {
                            buttonLabel = "🔒 " + buttonLabel;
                            buttonData = "/sampler premium";
                        }
                        else
                            buttonLabel = "⭐ " + buttonLabel;
                    }

                    if (samplerName == settings.Sampler)
                    {
                        buttonLabel += " ✅";
                    }

                    keyboardRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new TL.KeyboardButtonCallback { text = buttonLabel, data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                        }
                    });
                }
            }

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "Default", data = System.Text.Encoding.UTF8.GetBytes("/sampler default") }
                }
            });

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/sampler cancel") }
                }
            });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard
            await t.SendMessageAsync(
                text: "Select a sampler:",
                replyInlineMarkup: inlineKeyboard,
                replyToMessage: message
            );
        }

        [CommandDescription("Change current AI model.")]
        [CommandArguments("")]
        private static async Task CmdModel(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxMessages.SendModelList(t, user, message);
        }

        private static async Task CmdBroadcast(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessage: message
                );

                return;
            }

            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /ban <uid>\r\n  /ban @username",
                    replyToMessage: message
                );

                return;
            }

            long news_id = 0;

            if (!long.TryParse(argument, out news_id))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            TL.Message? newMessage = await t.GetReplyMessage(message);
            TL.InputPhoto? inputPhoto = null;

            if (newMessage is not null && newMessage.media is MessageMediaPhoto { photo: Photo photo })
            {
                inputPhoto = new InputPhoto
                {
                    id = photo.ID,
                    access_hash = photo.access_hash,
                    file_reference = photo.file_reference
                };
            }

            _ = FoxNews.BroadcastNewsItem(t, news_id, inputPhoto);
        }

        private static async Task CmdBan(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessage: message
                );

                return;
            }

            if (String.IsNullOrEmpty(argument)) {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /ban <uid>\r\n  /ban @username",
                    replyToMessage: message
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
                throw new ArgumentException("You must specify a username.");

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessage: message
                );

                return;
            }

            string? banMessage = null;

            if (args.Length == 2)
                banMessage = args[1];

            if (banUser.CheckAccessLevel(AccessLevel.PREMIUM) || banUser.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You can't ban an admin or premium user!",
                    replyToMessage: message
                );

                return;
            }

            if (banUser.GetAccessLevel() == AccessLevel.BANNED)
            {
                await t.SendMessageAsync(
                    text: "❌ User is already banned.",
                    replyToMessage: message
                );

                return;
            }

            await banUser.Ban(reasonMessage: banMessage);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} banned.",
                replyToMessage: message
            );
        }

        [CommandDescription("Set your negative prompt for this chat or group.  Leave blank to clear.")]
        [CommandArguments("[<negative prompt>]")]
        private static async Task CmdSetNegative(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
                settings.NegativePrompt = argument; //.Replace("\n", ", ");
            else
                settings.NegativePrompt = "";

            await settings.Save();

            await t.SendMessageAsync(
                text: (settings.NegativePrompt.Length > 0 ? $"✅ Negative prompt set." : "✅ Negative prompt cleared."),
                replyToMessage: message
            );
        }

        private static async Task CmdInfo(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var sb = new StringBuilder();
            var selectedUser = user;

            if (!string.IsNullOrEmpty(argument))
            {
                if (!user.CheckAccessLevel(AccessLevel.ADMIN))
                {
                    await t.SendMessageAsync(
                        text: "❌ You must be an admin to view another user's info.",
                        replyToMessage: message
                    );

                    return;
                }

                selectedUser = await FoxUser.ParseUser(argument);

                if (selectedUser is null)
                {
                    await t.SendMessageAsync(
                        text: "❌ Unable to parse user ID.",
                        replyToMessage: message
                    );

                    return;
                }
            }
            else
            {
                var uptime = DateTime.Now - FoxMain.startTime;

                // Get memory usage
                Process currentProcess = Process.GetCurrentProcess();
                long usedMemory = currentProcess.WorkingSet64;

                // Get thread count and active threads
                int threadCount = currentProcess.Threads.Count;
                int activeThreads = currentProcess.Threads.Cast<ProcessThread>().Count(t => t.ThreadState == System.Diagnostics.ThreadState.Running);

                // Get system uptime
                var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);


                // Collecting information into a single StringBuilder
                sb.AppendLine($"🦊 Bot info:\n\nVersion: " + FoxMain.GetVersion());
                sb.AppendLine($"Bot Uptime: {uptime.ToPrettyFormat()}");
                sb.AppendLine($"System Uptime: {systemUptime.ToPrettyFormat()}");
                sb.AppendLine($"Memory Used: {usedMemory / 1024 / 1024} MB");
                sb.AppendLine($"Threads (running/total): {activeThreads} / {threadCount}");

                sb.AppendLine("\nUser Info:\n");
            }


            sb.AppendLine(await FoxMessages.BuildUserInfoString(selectedUser));


            long imageCount = 0;
            long imageBytes = 0;
            long userCount = 0;
            DateOnly oldestImage = DateOnly.FromDateTime(DateTime.Now);

            // Only show global stats if no user was specified
            if (string.IsNullOrEmpty(argument))
            {

                sb.AppendLine("Global Stats:\n");

                using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await connection.OpenAsync();

                    MySqlCommand sqlcmd;

                    sqlcmd = new MySqlCommand("SELECT COUNT(id) as image_count, MIN(date_added) as oldest_image, SUM(filesize) AS image_bytes FROM images WHERE type = 'OUTPUT'", connection);

                    using (var reader = await sqlcmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            imageCount = reader.IsDBNull(reader.GetOrdinal("image_count")) ? 0 : reader.GetInt64("image_count");
                            imageBytes = reader.IsDBNull(reader.GetOrdinal("image_bytes")) ? 0 : reader.GetInt64("image_bytes");
                            oldestImage = reader.IsDBNull(reader.GetOrdinal("oldest_image")) ? oldestImage : reader.GetDateOnly("oldest_image");
                        }
                    }

                    sqlcmd = new MySqlCommand("SELECT COUNT(id) as user_count FROM users", connection);

                    using (var reader = await sqlcmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            userCount = reader.IsDBNull(reader.GetOrdinal("user_count")) ? 0 : reader.GetInt64("user_count");
                        }
                    }
                }

                sb.AppendLine($"Oldest Image: {oldestImage.ToString("MMMM d yyyy")}");
                sb.AppendLine();
                sb.AppendLine($"Total Images: {imageCount} ({FoxMessages.FormatBytes(imageBytes)})");
                sb.AppendLine($"Total Users: {userCount}");
            }

            await t.SendMessageAsync(
                text: sb.ToString(),
                replyToMessage: message
            );
        }

        [CommandDescription("Set or view your prompt for this chat or group.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdSetPrompt(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
            {

                settings.Prompt = argument; //.Replace("\n", ", ");

                await settings.Save();

                await t.SendMessageAsync(
                    text: $"✅ Prompt set.",
                    replyToMessage: message
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: $"🖤Current prompt: " + settings.Prompt,
                    replyToMessage: message
                );
            }
        }

        [CommandDescription("Set the seed value for the next generation. Default: -1 (random)")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetSeed(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            int seed = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "Current Seed: " + settings.Seed,
                    replyToMessage: message
                );
                return;
            }

            if (!int.TryParse(argument, out seed))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            settings.Seed = seed;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Seed set to {seed}.",
                replyToMessage: message
            );
        }

        [CommandDescription("Set or view your CFG Scale for this chat or group. Range 0 - 99.0.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetCFG(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "Current CFG Scale: " + settings.CFGScale,
                    replyToMessage: message
                );
                return;
            }

            if (!decimal.TryParse(argument, out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 99)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 99.0.",
                    replyToMessage: message
                );

                return;
            }

            settings.CFGScale = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ CFG Scale set to {cfgscale}.",
                replyToMessage: message
            );
        }

        [CommandDescription("Set or view your Denoise Strength for this chat or group, used only by img2img. Range 0 - 1.0.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetDenoise(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var stepstr = message.message.Split(' ');

            if (stepstr.Count() < 2)
            {
                await t.SendMessageAsync(
                    text: "Current Denoising Strength: " + settings.DenoisingStrength,
                    replyToMessage: message
                );
                return;
            }

            if (stepstr.Count() > 2 || !decimal.TryParse(stepstr[1], out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 1)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 1.0.",
                    replyToMessage: message
                );

                return;
            }

            settings.DenoisingStrength = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Denoising Strength set to {cfgscale}.",
                replyToMessage: message
            );
        }

        [CommandDescription("Set or view your sampler steps for this chat or group.  Range varies based on load and account type.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetSteps(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            int steps = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var stepstr = message.message.Split(' ');

            if (stepstr.Count() < 2)
            {
                await t.SendMessageAsync(
                    text: "Current steps value: " + settings.steps,
                    replyToMessage: message
                );
                return;
            }

            if (stepstr.Count() > 2 || !stepstr[1].All(char.IsDigit))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide an integer value.",
                    replyToMessage: message
                );

                return;
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            steps = Int16.Parse(stepstr[1]);

            if (steps > 20 && !isPremium)
            {
                await t.SendMessageAsync(
                    text: "❌ Only members can exceed 20 steps.\r\n\r\nPlease consider a /membership",
                    replyToMessage: message
                );

                if (settings.steps > 20)
                {
                    settings.steps = 20;

                    await settings.Save();
                }
                return;
            }
            else if (steps < 1 || (steps > 30 && !user.CheckAccessLevel(AccessLevel.ADMIN)))
            {
                await t.SendMessageAsync(
                    text: "❌ Value must be above 1 and below 30.",
                    replyToMessage: message
                );

                return;
            }

            settings.steps = steps;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Steps set to {steps}.",
                replyToMessage: message
            );
        }

        [CommandDescription("Show all of your currently configured settings for this chat or group.")]
        private static async Task CmdCurrent(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);
            await settings.Save(); // Save the settings just in case this is a new chat, to init the defaults so we don't look like a liar later.

            Message waitMsg = await t.SendMessageAsync(
                text: $"🖤Prompt: {settings.Prompt}\r\n" +
                      $"🐊Negative: {settings.NegativePrompt}\r\n" +
                      $"🖥️Size: {settings.Width}x{settings.Height}\r\n" +
                      $"🪜Sampler: {settings.Sampler} ({settings.steps} steps)\r\n" +
                      $"🧑‍🎨CFG Scale: {settings.CFGScale}\r\n" +
                      $"👂Denoising Strength: {settings.DenoisingStrength}\r\n" +
                      $"🧠Model: {settings.Model}\r\n" +
                      $"🌱Seed: {settings.Seed}\r\n",
                replyToMessage: message
            );
        }

        [CommandDescription("Cancel all pending requests.")]
        [CommandArguments("")]
        private static async Task CmdCancel(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            int count = 0;

            var cancelMsg = await t.SendMessageAsync(
                text: $"⏳ Cancelling...",
                replyToMessage: message
            );

            List<ulong> pendingIds = new List<ulong>();

            var matchingItems = FoxQueue.fullQueue.FindAll(item => !item.IsFinished() && item.User?.UID == user.UID);

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

        [CommandDescription("Change the size of the output, e.g. /setsize 768x768")]
        [CommandArguments("<width>x<height>")]
        private static async Task CmdSetSize(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            int width;
            int height;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "🖥️ Current size: " + settings.Width + "x" + settings.Height,
                    replyToMessage: message
                );
                return;
            }

            var args = argument.ToLower().Split("x");

            if (args.Length != 2 || args[0] is null || args[1] is null ||
                !int.TryParse(args[0].Trim(), out width) || !int.TryParse(args[1].Trim(), out height))
            {
                await t.SendMessageAsync(
                    text: "❌ Value must be in the format of [width]x[height].  Example: /setsize 768x768",
                    replyToMessage: message
                );
                return;
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if ((width < 512 || height < 512) && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Dimension should be at least 512 pixels.",
                    replyToMessage: message
                );
                return;
            }

            if ((width > 1024 || height > 1024) && !isPremium)
            {
                await t.SendMessageAsync(
                    text: "❌ Only premium users can exceed 1024 pixels in any dimension.\n\nPlease consider becoming a premium member: /donate",
                    replyToMessage: message
                );
                return;
            } else if ((width * height) > 3686400 && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Total image pixel count cannot be greater than 1920x1920.",
                    replyToMessage: message
                );
                return;
            }

            var msgString = "";

            /*
            (int normalizedWidth, int normalizedHeight) = FoxImage.NormalizeImageSize(width, height);

            if (normalizedWidth != width || normalizedHeight != height)
            {
                msgString += $"⚠️ For optimal performance, your setting has been adjusted to: {normalizedWidth}x{normalizedHeight}.\r\n\r\n";
                msgString += $"⚠️To override, type /setsize {width}x{height} force.  You may receive less favorable queue priority.\r\n\r\n";

                width = normalizedWidth;
                height = normalizedHeight;

            } */

            msgString += $"✅ Size set to: {width}x{height}";

            settings.Width = (uint)width;
            settings.Height = (uint)height;

            await settings.Save();

            await t.SendMessageAsync(
                text: msgString,
                replyToMessage: message
            );;
        }
    }
}
