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
using System.Drawing;
using MySqlConnector;
using WTelegram;
using TL;

namespace makefoxsrv
{
    internal class FoxCommandHandler
    {
        private static readonly Dictionary<string, Func<FoxTelegram, TL.Message,  FoxUser, String?, Task>> CommandMap = new Dictionary<string, Func<FoxTelegram, TL.Message, FoxUser, String?, Task>>
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
            //{ "/select",      CmdSelect },
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
            //--------------- -----------------
            { "/cancel",      CmdCancel },
            //--------------- -----------------
            { "/donate",      CmdDonate },
            //--------------- -----------------
            { "/info",        CmdInfo },
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

            var commandHandler = FindBestMatch(command);

            if (commandHandler is not null)
            {
                var fUser = await FoxUser.GetByTelegramUser(t.User, true);

                if (fUser is null)
                {
                    fUser = await FoxUser.CreateFromTelegramUser(t.User);

                    if (fUser is null)
                        throw new Exception("Unable to create new user");
                }
                else
                    await fUser.UpdateTimestamps();

                await commandHandler(t, message, fUser, argument);
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

        public static int CalculateRewardDays(int amountInCents)
        {
            int baseDays = 30; // Base days for $10
            int targetDaysForMaxAmount = 365; // Target days for $100

            // Convert amount to dollars from cents for calculation
            decimal amountInDollars = amountInCents / 100m;

            if (amountInCents == 500)
            {
                return 10; // Directly return 10 days for $5
            }
            else if (amountInDollars <= 10)
            {
                return baseDays;
            }
            else if (amountInDollars >= 100)
            {
                // Calculate days as if $100 is given for each $100 increment
                decimal multiplesOverMax = amountInDollars / 100;
                return (int)Math.Round(targetDaysForMaxAmount * multiplesOverMax);
            }
            else
            {
                // Linear interpolation for amounts between $10 and $100
                decimal daysPerDollar = (targetDaysForMaxAmount - baseDays) / (100m - 10m);
                return (int)Math.Round(baseDays + (amountInDollars - 10) * daysPerDollar);
            }
        }

        [CommandDescription("Make a one-time monetary donation")]
        [CommandArguments("")]
        private static async Task CmdDonate(FoxTelegram t, Message message, FoxUser user, String? argument)
        {

            if (string.IsNullOrEmpty(FoxMain.settings?.TelegramPaymentToken))
                throw new Exception("Donations are currently disabled. (token not set)");

            // Define donation amounts in whole dollars
            int[] donationAmounts = new int[] { 5, 10, 20, 40, 60, 100 };

            // Initialize a list to hold TL.KeyboardButtonRow for each row of buttons
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            // List to accumulate buttons for the current row
            List<TL.KeyboardButtonCallback> currentRowButtons = new List<TL.KeyboardButtonCallback>();

            // Loop through the donation amounts and create buttons
            for (int i = 0; i < donationAmounts.Length; i++)
            {
                int amountInCents = donationAmounts[i] * 100;
                int days = CalculateRewardDays(amountInCents);
                string buttonText = $"💳 ${donationAmounts[i]} ({days} days)";
                string callbackData = $"/donate {donationAmounts[i]} {days}";

                currentRowButtons.Add(new TL.KeyboardButtonCallback { text = buttonText, data = System.Text.Encoding.UTF8.GetBytes(callbackData) });

                // Every two buttons or at the end, add the current row to buttonRows and start a new row
                if ((i + 1) % 2 == 0 || i == donationAmounts.Length - 1)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });
                    currentRowButtons = new List<TL.KeyboardButtonCallback>(); // Reset for the next row
                }
            }

            // Add lifetime access button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "✨💰 💳 $600 (Lifetime Access!) 💰✨", data = System.Text.Encoding.UTF8.GetBytes("/donate 600 lifetime") }
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

            var msg = @"
<b>Support Our Service - Unlock Premium Access</b>

Thank you for considering a donation to our platform. Your support is crucial for the development and maintenance of our service.

Every <b>$10 US Dollars</b> spent grants 30 days of premium access, with rewards increasing for larger contributions.

<b>Legal:</b>

Our service is provided on a best-effort basis, without express or implied warranties.  We reserve the right to modify or discontinue features and limits at any time. <b>Donations are final and non-refundable.</b>

<a href=""https://telegra.ph/Makefoxbot-Tier-Limits-02-22"">Click for more information...</a>

We sincerely appreciate your support and understanding. Your contribution directly impacts our ability to maintain and enhance our service, ensuring a robust platform for all users.";

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            await t.SendMessageAsync(
                text: msg,
                replyInlineMarkup: inlineKeyboard,
                entities: entities,
                disableWebPagePreview: true
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

                await FoxQueue.Enqueue(q);
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("Error: " + ex.Message);
            }
        }

        //[CommandDescription("Select your last uploaded image as the input for /img2img")]
        //private static async Task CmdSelect(FoxTelegram t, Message message, FoxUser user, String? argument)
        //{
        //    var img = await FoxImage.LoadLastUploaded(user, t.Chat.ID);

        //    if (img is null)
        //    {
        //        await botClient.SendMessageAsync(
        //                peer: tPeer,
        //                text: "❌ Error: You must upload an image first.",
        //                reply_to_msg_id: message.ID
        //                );

        //        return;
        //    }

        //    var settings = await FoxUserSettings.GetTelegramSettings(user, tUser, tChat);

        //    settings.selected_image = img.ID;

        //    await settings.Save();

        //    try {
        //        Message waitMsg = await botClient.SendMessageAsync(
        //            peer: tChat,
        //            text: "✅ Image saved and selected as input for /img2img",
        //            reply_to_msg_id: (int)(img.TelegramMessageID ?? message.ID)
        //        );
        //    } catch {
        //        Message waitMsg = await botClient.SendMessageAsync(
        //            peer: tChat,
        //            text: "✅ Image saved and selected as input for /img2img",
        //            reply_to_msg_id: message.ID
        //        );
        //    }

        //}

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
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
            {
                settings.prompt = argument; //.Replace("\n", ", ");
                await settings.Save();
            }

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, message.ID);

                return; // User must agree to the terms before they can use this command.
            }

            if (settings.selected_image <= 0 || await FoxImage.Load(settings.selected_image) is null)
            {
                await t.SendMessageAsync(
                    text: "❌You must upload or /select an image first to use img2img functions.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (String.IsNullOrEmpty(settings.prompt))
            {
                await t.SendMessageAsync(
                    text: "❌You must specify a prompt!  Please seek /help",
                    replyToMessageId: message.ID
                );

                return;
            }

            int q_limit = 1;
            switch (user.GetAccessLevel())
            {
                case AccessLevel.ADMIN:
                    q_limit = 20;
                    break;
                case AccessLevel.PREMIUM:
                    q_limit = 3;
                    break;
            }

            if (await FoxQueue.GetCount(user) >= q_limit)
            {
                await t.SendMessageAsync(
                    text: $"❌ Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (await FoxWorker.GetWorkersForModel(settings.model ?? "") is null)
            {
                await t.SendMessageAsync(
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.model}).\r\n\r\nPlease try again later or select a different /model.",
                    replyToMessageId: message.ID
                );

                return;
            }

            (int position, int totalItems) = FoxQueue.GetNextPosition(user, false);

            Message waitMsg = await t.SendMessageAsync(
                text: $"⏳ Adding to queue ({position} of {totalItems})...",
                replyToMessageId: message.ID
            );

            var q = await FoxQueue.Add(t, user, settings, FoxQueue.QueueType.IMG2IMG, waitMsg.ID, message.ID);
            if (q is null)
                throw new Exception("Unable to add item to queue");

            await FoxQueue.Enqueue(q);

        }

        [CommandDescription("Run a standard txt2img generation.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdGenerate(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(argument))
            {
                settings.prompt = argument; //.Replace("\n", ", ");
                await settings.Save();
            }

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, message.ID);

                return; // User must agree to the terms before they can use this command.
            }

            if (String.IsNullOrEmpty(settings.prompt))
            {
                await t.SendMessageAsync(
                    text: "❌You must specify a prompt!  Please seek /help",
                    replyToMessageId: message.ID
                );

                return;
            }

            int q_limit = 1;
            switch (user.GetAccessLevel())
            {
                case AccessLevel.ADMIN:
                    q_limit = 20;
                    break;
                case AccessLevel.PREMIUM:
                    q_limit = 3;
                    break;
            }

            if (await FoxQueue.GetCount(user) >= q_limit)
            {
                await t.SendMessageAsync(
                    text: $"❌Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (await FoxWorker.GetWorkersForModel(settings.model) is null)
            {
                await t.SendMessageAsync(
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.model}).\r\n\r\nPlease try again later or select a different /model.",
                    replyToMessageId: message.ID
                );

                return;
            }

            (int position, int totalItems) = FoxQueue.GetNextPosition(user, false);

            Message waitMsg = await t.SendMessageAsync(
                text: $"⏳ Adding to queue ({position} of {totalItems})...",
                replyToMessageId: message.ID
            );

            var q = await FoxQueue.Add(t, user, settings, FoxQueue.QueueType.TXT2IMG, waitMsg.ID, message.ID);
            if (q is null)
                throw new Exception("Unable to add item to queue");            

            await FoxQueue.Enqueue(q);           
        }

        [CommandDescription("Change current AI model.")]
        [CommandArguments("")]
        private static async Task CmdModel(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var models = await FoxWorker.GetAvailableModels(); // Use the GetModels function here

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

            foreach (var model in models)
            {
                string modelName = model.Key;
                int workerCount = model.Value.Count; // Assuming you want the count of workers per model

                var buttonLabel = $"{modelName} ({workerCount})";
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
                text: "Select a model:",
                replyInlineMarkup: inlineKeyboard
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
            await t.SendMessageAsync(
                text: $"🦊 Version: " + FoxMain.GetVersion(),
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
                    text: "❌ Only premium users can exceed 20 steps.\r\n\r\nConsider making a donation: /donate",
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
                      $"🪜Sampler Steps: {settings.steps}\r\n" +
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
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

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
                    text: "❌ Dimenion should be at least 512 pixels.",
                    replyToMessageId: message.ID
                );
                return;
            }

            if ((width > 1024 || height > 1024) && !user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                await t.SendMessageAsync(
                    text: "❌ Only premium users can exceed 1024 pixels in any direction.",
                    replyToMessageId: message.ID
                );
                return;
            } else if ((width > 1280 || height > 1280) && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Dimension cannot be greater than 1280.",
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
