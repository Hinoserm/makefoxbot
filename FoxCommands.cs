using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot;
using System.Threading;
using System.Reflection;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;
using System.Reflection.Metadata;

namespace makefoxbot
{
    internal class FoxCommandHandler
    {
        private static readonly Dictionary<string, Func<ITelegramBotClient, Message, CancellationToken, FoxUser, String?, Task>> CommandMap = new Dictionary<string, Func<ITelegramBotClient, Message, CancellationToken, FoxUser, String?, Task>>
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
            //--------------- -----------------
            { "/help",        CmdHelp },
        };

        public static async Task HandleCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {

            if (message is null || botClient is null || message.From is null)
                throw new ArgumentNullException();

            if (message.Text is null || message.Text.Length < 2)
                return;

            if (message.Text[0] != '/')
                return; // Not a command, skip it.

            var args = message.Text.Split(new char[] { ' ', '\n' }, 2);
            var command = args[0];

            var c = command.Split('@', 2);
            if (c.Count() == 2)
            {
                if (c[1] != Program.me.Username)
                    return; // Not for us, skip it.

                command = c[0];
            }

            var argument = (args.Count() >= 2 ? args[1].TrimStart() : null);

            var commandHandler = FindBestMatch(command);

            if (commandHandler is not null)
            {
                var user = await FoxUser.GetByTelegramUser(message.From);

                if (user is null)
                {
                    user = await FoxUser.CreateFromTelegramUser(message.From);

                    if (user is null)
                        throw new Exception("Unable to create new user");

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: $"🦊Hello! You are user #{user.UID}!\r\nThis bot is a work in progress.\r\n\r\nFor more info, type /help",
                        cancellationToken: cancellationToken
                    );
                }
                else
                    await user.UpdateTimestamps();

                await commandHandler(botClient, message, cancellationToken, user, argument);
            }
            else if (message.From.Id == message.Chat.Id)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"🦊 I'm sorry, I didn't understand that command.  Try /help.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }
        }
        public static string GetCommandDescription(Func<ITelegramBotClient, Message, CancellationToken, FoxUser, String?, Task> commandFunction)
        {
            var methodInfo = commandFunction.Method;
            var attribute = methodInfo.GetCustomAttribute<CommandDescriptionAttribute>();
            return attribute?.Description;
        }

        public static string GetCommandArguments(Func<ITelegramBotClient, Message, CancellationToken, FoxUser, String?, Task> commandFunction)
        {
            var methodInfo = commandFunction.Method;
            var attribute = methodInfo.GetCustomAttribute<CommandArgumentsAttribute>();
            return attribute?.Arguments;
        }

        public static string GetCommandDescription(string commandName)
        {
            var methodInfo = typeof(FoxCommandHandler).GetMethod(commandName, BindingFlags.NonPublic | BindingFlags.Static);
            if (methodInfo != null)
            {
                var attribute = methodInfo.GetCustomAttribute<CommandDescriptionAttribute>();
                return attribute?.Description;
            }
            return null;
        }

        private static Func<ITelegramBotClient, Message, CancellationToken, FoxUser, String?, Task> FindBestMatch(string command)
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

        public static BotCommand[] GenerateTelegramBotCommands()
        {
            var commandList = CommandMap
                .GroupBy(pair => pair.Value, pair => pair.Key.TrimStart('/'))
                .Select(group => new BotCommand
                {
                    Command = group.OrderByDescending(cmd => cmd.Length).First(),
                    Description = GetCommandDescription(group.Key)
                })
                .Where(cmd => !string.IsNullOrWhiteSpace(cmd.Description))
                .OrderBy(cmd => cmd.Command) // Order commands alphabetically
                .ToArray();

            return commandList;
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

        public static async Task HandleCallback(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user) {
            var a = update.CallbackQuery.Data.Split(" ", 2);
            var command = a[0];

            var argument = (a[1] is not null ? a[1] : "");


            switch (command) {
                case "/info":
                    await CallbackCmdInfo(botClient, update, cancellationToken, user, argument);
                    break;
                case "/download":
                    await CallbackCmdDownload(botClient, update, cancellationToken, user, argument);
                    break;
                case "/select":
                    await CallbackCmdSelect(botClient, update, cancellationToken, user, argument);
                    break;
            }
        }

        private static async Task CallbackCmdInfo(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
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

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👍", "/vote up " + q.id),
                    InlineKeyboardButton.WithCallbackData("👎", "/vote down " + q.id),
                    InlineKeyboardButton.WithCallbackData("💾", "/download " + q.id),
                    InlineKeyboardButton.WithCallbackData("🎨", "/select " + q.id),
                    
                },
                //new[]
                //{
                //    InlineKeyboardButton.WithCallbackData("Show Details", "/info " + q.id),
                //}
            });

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id);

            if (q is not null && q.TelegramChatID == update.CallbackQuery.Message.Chat.Id)
            {
                Message waitMsg = await botClient.EditMessageTextAsync(
                    chatId: update.CallbackQuery.Message.Chat.Id,
                    text: $"🖤Prompt: {q.settings.prompt}\r\n" +
                          $"🐊Negative: {q.settings.negative_prompt}\r\n" +
                          $"🖥️ Size: {q.settings.width}x{q.settings.height}\r\n" +
                          $"🪜Sampler Steps: {q.settings.steps}\r\n" +
                          $"🧑‍🎨CFG Scale: {q.settings.cfgscale}\r\n" +
                          $"👂Denoising Strength: {q.settings.denoising_strength}\r\n" +
                          $"🌱Default Seed: {q.settings.seed}\r\n",
                    messageId: update.CallbackQuery.Message.MessageId,
                    replyMarkup: inlineKeyboard,
                    cancellationToken: cancellationToken
                );
            }
        }

        private static async Task CallbackCmdDownload(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
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

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "Transferring, please wait...");

            if (q is not null && q.TelegramChatID == update.CallbackQuery.Message.Chat.Id)
            {
                var img = await q.LoadOutputImage();

                if (img is null)
                    throw new Exception("Unable to locate image");

                if (img.TelegramFullFileID is not null)
                {
                    Message message = await botClient.SendDocumentAsync(
                        chatId: q.TelegramChatID,
                        document: InputFile.FromFileId(img.TelegramFullFileID),
                        cancellationToken: cancellationToken
                        );
                }
                else if (img is not null)
                {

                    Message message = await botClient.SendDocumentAsync(
                        chatId: q.TelegramChatID,
                        document: InputFile.FromStream(new MemoryStream(img.Image), q.link_token + ".png"),
                        cancellationToken: cancellationToken
                        );

                    await img.SaveFullTelegramFileIds(message.Document.FileId, message.Document.FileUniqueId);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "❌ Error: Unable to locate image file.",
                        cancellationToken: cancellationToken
                        );
                }
            }
        }

        private static async Task CallbackCmdSelect(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
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

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "Selected");

            if (q is not null && q.TelegramChatID == update.CallbackQuery.Message.Chat.Id)
            {
                var settings = await FoxUserSettings.GetTelegramSettings(user, update.CallbackQuery.From, update.CallbackQuery.Message.Chat);

                if (q.image_id is null)
                    return;

                settings.selected_image = (ulong)q.image_id;

                await settings.Save();
                
                try
                {
                    Message waitMsg = await botClient.SendTextMessageAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "✅ Image selected as input for /img2img",
                        cancellationToken: cancellationToken
                        );
                }
                catch { }
            }
        }

        [CommandDescription("It's delicious!")]
        [CommandArguments("<pants level> [<pizza>]")]
        private static async Task CmdTest(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            settings.prompt = "cute male fox wearing (jeans), holding a (slice of pizza), happy, smiling, (excited), energetic, sitting, solo, vibrant and surreal background, (80's theme), masterpiece, perfect anatomy, shoes";
            settings.negative_prompt = "(female), penis, nsfw, eating, boring_e621_fluffyrock_v4, deformityv6, easynegative, bad anatomy, multi limb, multi tail, ((human)), text, signature, watermark, logo, writing, words";
            settings.cfgscale = 10M;
            settings.steps = 20;
            settings.width = 768;
            settings.height = 768;
            settings.seed = -1;

            Message waitMsg = await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"⏳👖🍕🦊 Please wait...",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );

            await FoxQueue.Add(user, settings, "TXT2IMG", waitMsg.MessageId, message.MessageId);

            Program.semaphore.Release();
        }

        private static async Task CmdHelp(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
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

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: string.Join(Environment.NewLine, helpEntries),
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Run an img2img generation.  Requires you to have previously uploaded an image.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdImg2Img(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (!string.IsNullOrEmpty(argument))
            {
                settings.prompt = argument.Replace("\n", ", ");
                await settings.Save();
            }

            if (settings.selected_image <= 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must send an image first to use img2img functions.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            int q_limit = 2;
            switch (user.AccessLevel)
            {
                case "ADMIN":
                    q_limit = 20;
                    break;
                case "PREMIUM":
                    q_limit = 5;
                    break;
            }

            if (await FoxQueue.GetCountByUser(user) >= q_limit)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            Message waitMsg = await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,

                text: $"⏳ Adding to queue...",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );

            var q = await FoxQueue.Add(user, settings, "IMG2IMG", waitMsg.MessageId, message.MessageId);
            if (q is null)
                throw new Exception("Unable to add item to queue");

            await q.CheckPosition(); // Load the queue position and total.

            Program.semaphore.Release();

            try
            {
                await botClient.EditMessageTextAsync(
                    chatId: message.Chat.Id,
                    messageId: waitMsg.MessageId,
                    text: $"⏳ In queue ({q.position} of {q.total})..."
                );
            }
            catch { }

        }

        [CommandDescription("Run a standard txt2img generation.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdGenerate(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (!string.IsNullOrEmpty(argument))
            {
                settings.prompt = argument.Replace("\n", ", ");
                await settings.Save();
            }

            int q_limit = 2;
            switch (user.AccessLevel)
            {
                case "ADMIN":
                    q_limit = 20;
                    break;
                case "PREMIUM":
                    q_limit = 5;
                    break;
            }

            if (await FoxQueue.GetCountByUser(user) >= q_limit)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            Message waitMsg = await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"⏳ Adding to queue...",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );

            var q = await FoxQueue.Add(user, settings, "TXT2IMG", waitMsg.MessageId, message.MessageId);
            if (q is null)
                throw new Exception("Unable to add item to queue");

            await q.CheckPosition(); // Load the queue position and total.

            Program.semaphore.Release();

            try
            {
                await botClient.EditMessageTextAsync(
                    chatId: message.Chat.Id,
                    messageId: waitMsg.MessageId,
                    text: $"⏳ In queue ({q.position} of {q.total})..."
                );
            }
            catch { }
        }


        [CommandDescription("Set your negative prompt for this chat or group.  Leave blank to clear.")]
        [CommandArguments("[<negative prompt>]")]
        private static async Task CmdSetNegative(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (!string.IsNullOrEmpty(argument))
                settings.negative_prompt = argument.Replace("\n", ", ");
            else
                settings.negative_prompt = "";

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: (settings.negative_prompt.Length > 0 ? $"✅ Negative prompt set." : "✅ Negative prompt cleared."),
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Set or view your prompt for this chat or group.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdSetPrompt(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (!string.IsNullOrEmpty(argument))
            {

                settings.prompt = argument.Replace("\n", ", ");

                await settings.Save();

                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"✅ Prompt set.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"🖤Current prompt: " + settings.prompt,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }
        }

        [CommandDescription("Set or view your CFG Scale for this chat or group. Range 0 - 99.0.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetCFG(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Current CFG Scale: " + settings.cfgscale,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }
            
            if (!decimal.TryParse(argument, out cfgscale))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 99)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌Value must be between 0 and 99.0.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            settings.cfgscale = cfgscale;

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"✅ CFG Scale set to {cfgscale}.",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Set or view your Denoise Strength for this chat or group, used only by img2img. Range 0 - 1.0.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetDenoise(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            var stepstr = message.Text.Split(' ');

            if (stepstr.Count() < 2)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Current Denoising Strength: " + settings.denoising_strength,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (stepstr.Count() > 2 || !decimal.TryParse(stepstr[1], out cfgscale))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 1)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌Value must be between 0 and 1.0.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            settings.denoising_strength = cfgscale;

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"✅ Denoising Strength set to {cfgscale}.",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Set or view your sampler steps for this chat or group.  Range varies based on load and account type.")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetSteps(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            int steps = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            var stepstr = message.Text.Split(' ');

            if (stepstr.Count() < 2)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Current steps value: " + settings.steps,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (stepstr.Count() > 2 || !stepstr[1].All(char.IsDigit))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must provide an integer value.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            steps = Int16.Parse(stepstr[1]);

            if (steps < 5 || steps > 40)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌Value must be above 5 and below 40.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            settings.steps = steps;

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"✅ Steps set to {steps}.",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Show all of your currently configured settings for this chat or group.")]
        private static async Task CmdCurrent(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);
            await settings.Save(); // Save the settings just in case this is a new chat, to init the defaults so we don't look like a liar later.

            Message waitMsg = await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"🖤Prompt: {settings.prompt}\r\n" +
                      $"🐊Negative: {settings.negative_prompt}\r\n" +
                      $"🖥️Size: {settings.width}x{settings.height}\r\n" +
                      $"🪜Sampler Steps: {settings.steps}\r\n" +
                      $"🧑‍🎨CFG Scale: {settings.cfgscale}\r\n" +
                      $"👂Denoising Strength: {settings.denoising_strength}\r\n" +
                      $"🌱Default Seed: {settings.seed}\r\n",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Not Yet Implemented")]
        private static async Task CmdSetSize(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            int width;
            int height;

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "🖥️ Current size: " + settings.width + "x" + settings.height,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            var args = argument.ToLower().Split("x");

            if (args.Length != 2 || args[0] is null || args[1] is null ||
                !int.TryParse(args[0].Trim(), out width) || !int.TryParse(args[1].Trim(), out height))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Value must be in the format of [width]x[height].  Example: /setsize 768x768",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (width < 100 || height < 100)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Dimenion should be at least 100 pixels.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (width > 1280 || height > 1280)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Dimenion must be no greater than 1200.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            settings.width = (uint)width;
            settings.height = (uint)height;

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"✅ Size set to: " + settings.width + "x" + settings.height,
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }
    }
}
