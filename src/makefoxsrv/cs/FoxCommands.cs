﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Reflection.Metadata;
using System.Linq.Expressions;
using System.Drawing;
using MySqlConnector;
using WTelegram;
using TL;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.ComponentModel.Design;

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
        };

        public static async Task HandleCommand(FoxTelegram t, Message message)
        {
            var explicitlyNamed = (t.Chat is null);

            if (message is null)
                throw new ArgumentNullException();

            if (message.message is null || message.message.Length < 2)
                return;

            if (message.message[0] != '/')
                return; // Not a command, skip it.

            var args = message.message.Split(new char[] { ' ', '\n' }, 2);
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
                            replyToMessageId: message.ID
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
                        replyToMessageId: message.ID
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
                    replyToMessageId: message.ID
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

        private static Func<FoxTelegram, Message, FoxUser, String?, Task>? FindBestMatch(string command)
        {
            List<string> potentialMatches = new List<string>();

            foreach (var cmd in CommandMap.Keys)
            {
                if (cmd.StartsWith(command))
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
            if (CommandMap.ContainsKey(command))
            {
                return CommandMap[command];
            }

            // Find the shortest command in potential matches
            string shortestMatch = potentialMatches
                .OrderBy(s => s.Length)
                .FirstOrDefault();

            // Check if the command is a unique prefix
            bool isUniquePrefix = potentialMatches.All(s => !s.StartsWith(command) || s.Equals(shortestMatch));

            if (isUniquePrefix)
            {
                // Return the command if it's a unique prefix
                return CommandMap[shortestMatch];
            }
            else
            {
                // Ambiguous command
                return null;
            }
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


            string webUrl = $"{FoxMain.settings.WebRootUrl}tgapp/styles.php";

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
                disableWebPagePreview: true
            );
        }

        private static async Task CmdAdmin(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessageId: message.ID
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
                default:
                    await t.SendMessageAsync(
                        text: "❌ Unknown command.  Use one of these:\r\n  #uncache, #ban, #unban, #resetterms, #resettos",
                        replyToMessageId: message.ID
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
                buttons = new TL.KeyboardButtonWebView[]
                {
                    new() { text = "Custom Amount", url = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?tg=1&id={pSession.UUID}" }
                }
            });

            // Add cancel button on its own row at the end
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/donate cancel") }
                }
            });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() };

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
            sb.AppendLine(" - <b>High-Resolution Image Enhancements:</b> Members enjoy nearly unlimited enhancements, subject to fair usage limits.\n");

            sb.AppendLine(" - <b>Flexible Image Dimensions:</b> Create images in any shape and size up to 3.7 million pixels.\n");

            sb.AppendLine(" - <b>Prompt Assistance (Coming Soon!):</b> Access to a natural language AI assistant for prompt building.\n");

            sb.AppendLine(" - <b>Early Access:</b> Be the first to try new experimental models and features.\n");

            sb.AppendLine("<a href=\"https://telegra.ph/MakeFox-Membership-06-07\"><b>Click here for more information.</b></a>\n");

            sb.Append("<i>Note: Membership purchases are not tax-deductible.</i>");

            var msg = sb.ToString();

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            var sentMessage = await t.SendMessageAsync(
                text: msg,
                replyInlineMarkup: inlineKeyboard,
                entities: entities,
                disableWebPagePreview: true
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
                entities: entities

            );
        }

        [CommandDescription("It's delicious!")]
        [CommandArguments("<pants level> [<pizza>]")]
        private static async Task CmdTest(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, message.ID, 0);

                return; // User must agree to the terms before they can use this command.
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.prompt = "cute male fox wearing (jeans), holding a (slice of pizza), happy, smiling, (excited), energetic, sitting, solo, vibrant and surreal background, (80's theme), masterpiece, perfect anatomy, shoes";
            settings.negative_prompt = "(female), penis, nsfw, eating, boring_e621_fluffyrock_v4, deformityv6, easynegative, bad anatomy, multi limb, multi tail, ((human)), text, signature, watermark, logo, writing, words";
            settings.cfgscale = 10M;
            settings.steps = 20;
            settings.width = 768;
            settings.height = 768;
            settings.seed = -1;

            Message waitMsg = await t.SendMessageAsync(
                text: $"⏳👖🍕🦊 Please wait...",
                replyToMessageId: message.ID
            );

            try
            {
                if (waitMsg is null)
                    throw new Exception("Unable to send start message.  Giving up.");

                var q = await FoxQueue.Add(t, user, settings, FoxQueue.QueueType.TXT2IMG, waitMsg.ID, message.ID);

                FoxContextManager.Current.Queue = q;

                await FoxQueue.Enqueue(q);
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
                        replyToMessageId: message.ID
                        );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.selected_image = img.ID;

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
                replyToMessageId: message.ID
            );
        }

        private static async Task CmdStart(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            await FoxMessages.SendWelcome(t, user, message.ID);
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
                replyToMessageId: message.ID,
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
                        if (!user.CheckAccessLevel(AccessLevel.PREMIUM))
                        {
                            buttonLabel = "🔒 " + buttonLabel;
                            buttonData = "/sampler premium";
                        }
                        else
                            buttonLabel = "⭐ " + buttonLabel;
                    }

                    if (samplerName == settings.sampler)
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
                replyInlineMarkup: inlineKeyboard
            );
        }

        [CommandDescription("Change current AI model.")]
        [CommandArguments("")]
        private static async Task CmdModel(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var models = FoxModel.GetAvailableModels();

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

                if (modelName == settings.model)
                    buttonLabel += " ✅";

                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = buttonLabel, data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                    }
                });
            }

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") }
                }
            });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard
            await t.SendMessageAsync(
                text: "Select a model:\r\n\r\n (⭐ = Premium)",
                replyInlineMarkup: inlineKeyboard
            );
        }

        private static async Task CmdBroadcast(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /ban <uid>\r\n  /ban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            long news_id = 0;

            if (!long.TryParse(argument, out news_id))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.ID
                );

                return;
            }

            _= FoxNews.BroadcastNewsItem(news_id);
        }

        private static async Task CmdBan(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You must be an admin to use this command.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (String.IsNullOrEmpty(argument)) {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /ban <uid>\r\n  /ban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(new[] { ' ' }, 2, StringSplitOptions.None);

            if (args.Length < 1)
                throw new ArgumentException("You must specify a username.");

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
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

        [CommandDescription("Set your negative prompt for this chat or group.  Leave blank to clear.")]
        [CommandArguments("[<negative prompt>]")]
        private static async Task CmdSetNegative(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
                settings.negative_prompt = argument; //.Replace("\n", ", ");
            else
                settings.negative_prompt = "";

            await settings.Save();

            await t.SendMessageAsync(
                text: (settings.negative_prompt.Length > 0 ? $"✅ Negative prompt set." : "✅ Negative prompt cleared."),
                replyToMessageId: message.ID
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
                        replyToMessageId: message.ID
                    );

                    return;
                }

                selectedUser = await FoxUser.ParseUser(argument);

                if (selectedUser is null)
                {
                    await t.SendMessageAsync(
                        text: "❌ Unable to parse user ID.",
                        replyToMessageId: message.ID
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
            //long imageBytes = 0;
            long userCount = 0;

            // Only show global stats if no user was specified
            if (string.IsNullOrEmpty(argument))
            {

                sb.AppendLine("Global Stats:\n");

                using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await connection.OpenAsync();

                    MySqlCommand sqlcmd;

                    sqlcmd = new MySqlCommand("SELECT COUNT(id) as image_count FROM images", connection);

                    using (var reader = await sqlcmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            imageCount = reader.IsDBNull(reader.GetOrdinal("image_count")) ? 0 : reader.GetInt64("image_count");
                            //imageBytes = reader.IsDBNull(reader.GetOrdinal("image_bytes")) ? 0 : reader.GetInt64("image_bytes");
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

                sb.AppendLine($"Total Images: {imageCount}"); // ({FoxMessages.FormatBytes(imageBytes)})
                sb.AppendLine($"Total Users: {userCount}");
            }

            await t.SendMessageAsync(
                text: sb.ToString(),
                replyToMessageId: message.ID
            );
        }

        [CommandDescription("Set or view your prompt for this chat or group.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdSetPrompt(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
            {

                settings.prompt = argument; //.Replace("\n", ", ");

                await settings.Save();

                await t.SendMessageAsync(
                    text: $"✅ Prompt set.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: $"🖤Current prompt: " + settings.prompt,
                    replyToMessageId: message.ID
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
                    text: "Current Seed: " + settings.seed,
                    replyToMessageId: message.ID
                );
                return;
            }

            if (!int.TryParse(argument, out seed))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.ID
                );

                return;
            }

            settings.seed = seed;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Seed set to {seed}.",
                replyToMessageId: message.ID
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
                    text: "Current CFG Scale: " + settings.cfgscale,
                    replyToMessageId: message.ID
                );
                return;
            }

            if (!decimal.TryParse(argument, out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.ID
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 99)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 99.0.",
                    replyToMessageId: message.ID
                );

                return;
            }

            settings.cfgscale = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ CFG Scale set to {cfgscale}.",
                replyToMessageId: message.ID
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
                    text: "Current Denoising Strength: " + settings.denoising_strength,
                    replyToMessageId: message.ID
                );
                return;
            }

            if (stepstr.Count() > 2 || !decimal.TryParse(stepstr[1], out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.ID
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 1)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 1.0.",
                    replyToMessageId: message.ID
                );

                return;
            }

            settings.denoising_strength = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Denoising Strength set to {cfgscale}.",
                replyToMessageId: message.ID
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
                    replyToMessageId: message.ID
                );
                return;
            }

            if (stepstr.Count() > 2 || !stepstr[1].All(char.IsDigit))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide an integer value.",
                    replyToMessageId: message.ID
                );

                return;
            }

            steps = Int16.Parse(stepstr[1]);

            if (steps > 20 && !user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                await t.SendMessageAsync(
                    text: "❌ Only members can exceed 20 steps.\r\n\r\nPlease consider a /membership",
                    replyToMessageId: message.ID
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
                    replyToMessageId: message.ID
                );

                return;
            }

            settings.steps = steps;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Steps set to {steps}.",
                replyToMessageId: message.ID
            );
        }

        [CommandDescription("Show all of your currently configured settings for this chat or group.")]
        private static async Task CmdCurrent(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);
            await settings.Save(); // Save the settings just in case this is a new chat, to init the defaults so we don't look like a liar later.

            Message waitMsg = await t.SendMessageAsync(
                text: $"🖤Prompt: {settings.prompt}\r\n" +
                      $"🐊Negative: {settings.negative_prompt}\r\n" +
                      $"🖥️Size: {settings.width}x{settings.height}\r\n" +
                      $"🪜Sampler: {settings.sampler} ({settings.steps} steps)\r\n" +
                      $"🧑‍🎨CFG Scale: {settings.cfgscale}\r\n" +
                      $"👂Denoising Strength: {settings.denoising_strength}\r\n" +
                      $"🧠Model: {settings.model}\r\n" +
                      $"🌱Seed: {settings.seed}\r\n",
                replyToMessageId: message.ID
            );
        }

        [CommandDescription("Cancel all pending requests.")]
        [CommandArguments("")]
        private static async Task CmdCancel(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            int count = 0;

            var cancelMsg = await t.SendMessageAsync(
                text: $"⏳ Cancelling...",
                replyToMessageId: message.ID
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
                    FoxLog.WriteLine("Failed to edit message: " + msg_id.ToString());
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
                    text: "🖥️ Current size: " + settings.width + "x" + settings.height,
                    replyToMessageId: message.ID
                );
                return;
            }

            var args = argument.ToLower().Split("x");

            if (args.Length != 2 || args[0] is null || args[1] is null ||
                !int.TryParse(args[0].Trim(), out width) || !int.TryParse(args[1].Trim(), out height))
            {
                await t.SendMessageAsync(
                    text: "❌ Value must be in the format of [width]x[height].  Example: /setsize 768x768",
                    replyToMessageId: message.ID
                );
                return;
            }

            if (width < 512 || height < 512)
            {
                await t.SendMessageAsync(
                    text: "❌ Dimension should be at least 512 pixels.",
                    replyToMessageId: message.ID
                );
                return;
            }

            if ((width > 1024 || height > 1024) && !user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                await t.SendMessageAsync(
                    text: "❌ Only premium users can exceed 1024 pixels in any dimension.\n\nPlease consider becoming a premium member: /donate",
                    replyToMessageId: message.ID
                );
                return;
            } else if ((width * height) > 3686400 && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Total image pixel count cannot be greater than 1920x1920.",
                    replyToMessageId: message.ID
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

            settings.width = (uint)width;
            settings.height = (uint)height;

            await settings.Save();

            await t.SendMessageAsync(
                text: msgString,
                replyToMessageId: message.ID
            );;
        }
    }
}
