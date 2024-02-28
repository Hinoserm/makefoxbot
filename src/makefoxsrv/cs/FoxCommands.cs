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
using System.Linq.Expressions;
using System.Drawing;
using MySqlConnector;
using Telegram.Bot.Types.Payments;

namespace makefoxsrv
{
    internal class FoxCommandHandler
    {

        private static string[] text_help = {
@"Hi, I’m Makefoxbot. I can help you generate furry pictures through AI.

At the bottom left you’ll notice a blue menu button with various commands. Here’s what they do and the order in which you should use them to get started:

/setprompt followed by your prompt sets the image description. It’s generally good practice to use e621 tags separated by commas. You can also use parentheses for emphasis, e.g. (red ears:1.3) 

/setnegative sets your negative prompt, i.e. things you don’t want in your generation. It works the same as /setprompt. 

/setscale lets you define how closely the AI will follow your prompt. Default is at 7.5; generally you shouldn’t go above ~18 or you’ll get weird outputs.

/generate will then generate an image using the above input. If you’d like to skip the above you can also type /generate or /gen directly followed by your prompt.

If you prefer to use an input image with your prompt, just send me that image, define your prompt using /setprompt and /setnegative, then use /img2img to generate the output image.

/setdenoise lets you define how closely the AI will follow your input image. The default is at 0.75. 0 means the AI will copy the input image exactly, 1 means it will ignore it entirely.

All your settings and input images are stored by the bot until you replace them, so there is no need to input everything again for the same prompt. Either /generate or /img2img will work on their own.

Enjoy, and if you have any questions feel free to ask them in @toomanyfoxes

View a full list of commands: /commands",

@"/setprompt followed by your prompt sets the image description. It’s generally good practice to use e621 tags separated by commas, but other tags are also possible. You can also use parentheses for emphasis, e.g. (red ears:1.3), and you can group several traits within one pair of parentheses, which can be useful if you’re writing a prompt for something that involves multiple characters.

When using e621 tags, choose those that are both specific for what you want and reasonably frequent. The number will depend on specificity, but generally, a tag should have at least 200 occurrences on e621 for it to do you much good, ideally 1,000 and more. Replace underscores in tags with spaces and separate tags with commas.

The bot isn’t really built for free form/long sentence prompts, but occasionally they will work fine. 

Another thing to potentially include in your prompt are loras, which are specialized models that will improve outcomes for specific scenarios. The syntax for those is <lora:name:1>. A list of available loras is at xxx

Related commands: /setnegative, /setscale, /generate


/setnegative defines what you don’t want in your picture. It works the same way as /setprompt otherwise. In addition to specific tags you want to excuse, there are also some general models available that will prevent bad anatomy and other weird outcomes. Those are boring_e621_fluffyrock_v4, deformityv6, easynegative, bad anatomy, low quality.

BEWARE: If you put too much emphasis on a negative tag, it can sometimes have the opposite effect. Experiment and be aware that less may be more.

Related commands: /setprompt, /setscale


/setscale tells the AI how closely it should follow your text prompt. The default is at 7.5; lower values mean less weight, higher values mean more weight. This can be useful because the AI does not weigh all tags equally and sometimes you need to really push it to get a certain scenario, while in other cases it can be useful to make it a bit less eager to do so. Values above 18 are not recommended and will result in weird outcomes if chosen. 

Related commands: /setprompt, /setnegative",

@"/img2img allows you to generate a picture based on an input image and a text prompt. To use it, send me the input image, then set your prompt and negative prompt using /setprompt and /setnegative if you haven’t done so already. Following that, /img2img will then generate an image based on these inputs.

Related commands: /setdenoise, /select


/setdenoise lets you define how closely the AI will follow your input image. The default is at 0.75. 0 means the AI will copy the input image exactly, 1 means it will ignore it entirely. The best value will vary greatly depending on your prompt and input image, so experiment with this setting often.

This command only affects /img2img, it is not affected by /generate.

Related commands: /img2img, /select


/select turns your last output image into the input for your next img2img generation. This can be useful because you can approximate what you want through iterating img2img generations over multiple rounds of generations, discarding outputs that don’t show the desired outcome and keeping those that do.

Pushing the painter’s pallet button underneath any output image has the same effect and will select that image as the img2img input.

Related commands: /img2img, /setdenoise",

@"/setsize lets you define output image size in pixels. The default size is at 768x768. The maximum size you can request is at 1024x1024. Anything under 512x512 will generally result in low quality output.

If your input image isn’t a square, /img2img can result in distorted outputs, so it is best to adjust output image size to a similar proportion as your input image to avoid this effect. The input uses a widthxheight format.


/setseed lets you define the seed the AI uses to start generating the picture. If you don’t define it, the AI will choose it at random. The same input with the same seed and settings should always result in the same output. 

To return to random seed selection, use /setseed -1


/setsteps lets you define the number of steps the AI uses for generation. Generally, 15 to 20 steps is plenty; anything below or above will increase your chances of weird outputs."
        };

        private static string text_legal = @"
Legal:

This bot and the content generated are for research and educational purposes only.  For personal individual use only; do not sell generated content.  This system may generate harmful, incorrect or offensive content; you are responsible for following US law as well as your own local laws.";


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
            { "/select",      CmdSelect },
            //--------------- -----------------
            { "/start",       CmdWelcome },
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
                if (c[1] != FoxMain.me.Username)
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

                    //await botClient.SendTextMessageAsync(
                    //    chatId: message.Chat.Id,
                    //    text: $"🦊Hello! You are user #{user.UID}!\r\nThis bot is a work in progress.\r\n\r\nFor more info, type /help",
                    //    cancellationToken: cancellationToken
                    //);
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
                case "/model":
                    await CallbackCmdModel(botClient, update, cancellationToken, user, argument);
                    break;
                case "/help":
                    await CallbackCmdHelp(botClient, update, cancellationToken, user, argument);
                    break;
                case "/donate":
                    await CallbackCmdDonate(botClient, update, cancellationToken, user, argument);
                    break;
            }
        }

        private static async Task CallbackCmdModel(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
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

            if (argument == "cancel")
            {
                await botClient.EditMessageTextAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "✅ Operation cancelled.",
                        messageId: update.CallbackQuery.Message.MessageId,
                        cancellationToken: cancellationToken
                    );
            } else {
                if (argument == "default")
                    argument = "indigoFurryMix_v105Hybrid"; //Current default


                if (await FoxWorker.GetWorkersForModel(argument) is null)
                {
                    await botClient.EditMessageTextAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "❌ There are no workers currently available that can handle that model.  Please try again later.",
                        messageId: update.CallbackQuery.Message.MessageId,
                        cancellationToken: cancellationToken
                    );
                } else {
                    var settings = await FoxUserSettings.GetTelegramSettings(user, update.CallbackQuery.From, update.CallbackQuery.Message.Chat);

                    settings.model = argument;

                    settings.Save();

                    await botClient.EditMessageTextAsync(
                            chatId: update.CallbackQuery.Message.Chat.Id,
                            text: "✅ Model selected: " + argument,
                            messageId: update.CallbackQuery.Message.MessageId,
                            cancellationToken: cancellationToken
                        );
                }


            }

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id);

        }

        private static async Task CallbackCmdDonate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
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
                await botClient.EditMessageTextAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "✅ Operation cancelled.",
                        messageId: update.CallbackQuery.Message.MessageId,
                        cancellationToken: cancellationToken
                    );
            }
            else if(argument == "custom")
            {
                await botClient.EditMessageTextAsync(
                        chatId: update.CallbackQuery.Message.Chat.Id,
                        text: "🚧 Not Yet Implemented.",
                        messageId: update.CallbackQuery.Message.MessageId,
                        cancellationToken: cancellationToken
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


                var prices = new[] {
                    new LabeledPrice(days == -1 ? "Lifetime Access" : $"{days} Days Access", (int)(amount*100)),
                };

                await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id);

                await botClient.SendInvoiceAsync(
                    chatId: update.CallbackQuery.Message.Chat.Id,
                    title: $"One-Time Payment for User ID {user.UID}",
                    description: (days == -1 ? "Lifetime Access" : $"{days} Days Access"),
                    payload: $"PAY_{user.UID}_{days}", // Unique payload to identify the payment
                    providerToken: FoxMain.settings?.TelegramPaymentToken,
                    //startParameter: "payment",
                    currency: "USD",
                    prices: prices,
                    replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithPayment($"Pay ${amount}"))
                );

                try
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
                catch { } // We don't care if editing fails

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
                          $"🧠Model: {q.settings.model}\r\n" +
                          $"🌱Seed: {q.settings.seed}\r\n",
                    messageId: update.CallbackQuery.Message.MessageId,
                    replyMarkup: inlineKeyboard,
                    cancellationToken: cancellationToken
                );
            }
        }

        private static async Task CallbackCmdHelp(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken, FoxUser user, string? argument = null)
        {

            int help_id = 1;

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id);

            if (argument is null || argument.Length <= 0 || !int.TryParse(argument, out help_id))
                help_id = 1;

            if (help_id < 1 || help_id > text_help.Count())
                help_id = 1;

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("More Help", "/help " + (help_id+1)),
                    }
                });

            if (help_id >= text_help.Count())
                inlineKeyboard = null;

            await botClient.EditMessageReplyMarkupAsync(
                chatId: update.CallbackQuery.Message.Chat.Id,
                messageId: update.CallbackQuery.Message.MessageId,
                replyMarkup: null,
                cancellationToken: cancellationToken
            );

            await botClient.SendTextMessageAsync(
                chatId: update.CallbackQuery.Message.Chat.Id,
                text: text_help[help_id - 1],
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );
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
        private static async Task CmdDonate(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            if (string.IsNullOrEmpty(FoxMain.settings?.TelegramPaymentToken))
                throw new Exception("Donations are currently disabled. (token not set)");

            // Define donation amounts in whole dollars
            int[] donationAmounts = new int[] { 5, 10, 20, 40, 60, 100 };

            // Initialize a list to hold button rows, starting with the "Custom Amount" button
            List<InlineKeyboardButton[]> buttonRows = new List<InlineKeyboardButton[]>();

            // Temporary list to hold buttons for the current row
            List<InlineKeyboardButton> currentRow = new List<InlineKeyboardButton>();

            // Loop through the donation amounts and create buttons
            for (int i = 0; i < donationAmounts.Length; i++)
            {
                int amountInCents = donationAmounts[i] * 100;
                int days = CalculateRewardDays(amountInCents);
                currentRow.Add(InlineKeyboardButton.WithCallbackData($"💳 ${donationAmounts[i]} ({days} days)", $"/donate {donationAmounts[i]} {days}"));

                // Every two buttons or at the end, add the current row to buttonRows and start a new row
                if ((i + 1) % 2 == 0 || i == donationAmounts.Length - 1)
                {
                    buttonRows.Add(currentRow.ToArray());
                    currentRow = new List<InlineKeyboardButton>(); // Clear the currentRow by reinitializing it
                }
            }

            buttonRows.Add(new[] { InlineKeyboardButton.WithCallbackData("✨💰 💳 $600 (Lifetime Access!) 💰✨", "/donate 600 lifetime") });

            //buttonRows.Add(new[] { InlineKeyboardButton.WithCallbackData("Custom Amount", "/donate custom") });

            // Add a cancel button on its own row at the end
            buttonRows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "/donate cancel") });

            var msg = @"
<b>Support Our Service - Unlock Premium Access</b>

Thank you for considering a donation to our platform. Your support is crucial for the development and maintenance of our service.

Every <b>$10 US Dollars</b> spent grants 30 days of premium access, with rewards increasing for larger contributions.

<b>Legal:</b>

Our service is provided on a best-effort basis, without express or implied warranties.  We reserve the right to modify or discontinue features and limits at any time. <b>Donations are final and non-refundable.</b>

<a href=""https://telegra.ph/Makefoxbot-Tier-Limits-02-22"">Click for more information...</a>

We sincerely appreciate your support and understanding. Your contribution directly impacts our ability to maintain and enhance our service, ensuring a robust platform for all users.";

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: msg,
                replyMarkup: new InlineKeyboardMarkup(buttonRows),
                parseMode: ParseMode.Html,
                disableWebPagePreview: true
            );            
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

            FoxWorker.Ping();
        }

        [CommandDescription("Select your last uploaded image as the input for /img2img")]
        private static async Task CmdSelect(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var img = await FoxImage.LoadLastUploaded(user, message.Chat.Id);

            if (img is null)
            {
                await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "❌ Error: You must upload an image first.",
                        replyToMessageId: message.MessageId,
                        cancellationToken: cancellationToken
                        );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            settings.selected_image = img.ID;

            await settings.Save();

            try {
                Message waitMsg = await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "✅ Image saved and selected as input for /img2img",
                    replyToMessageId: (int)img.TelegramMessageID,
                    cancellationToken: cancellationToken
                );
            } catch {
                Message waitMsg = await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "✅ Image saved and selected as input for /img2img",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }

        }

        [CommandDescription("Show list of available commands")]
        private static async Task CmdCmdList(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
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


        private static async Task CmdWelcome(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("More Help", "/help 2"),
                    }
                });

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: text_help[0] + "\r\n" + text_legal,
                replyToMessageId: message.MessageId,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );

        }

        [CommandDescription("Show helpful information")]
        private static async Task CmdHelp(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("More Help", "/help 2"),
                    }
                });


            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: text_help[0],
                replyToMessageId: message.MessageId,
                replyMarkup: inlineKeyboard,
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
                settings.prompt = argument; //.Replace("\n", ", ");
                await settings.Save();
            }

            if (settings.selected_image <= 0 || await FoxImage.Load(settings.selected_image) is null)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must upload or /select an image first to use img2img functions.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
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

            if (await FoxQueue.GetCount(user.UID) >= q_limit)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            if (await FoxWorker.GetWorkersForModel(settings.model) is null)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.model}).\r\n\r\nPlease try again later or select a different /model.",
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

            FoxWorker.Ping();

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

        [CommandDescription("Change current AI model.")]
        [CommandArguments("")]
        private static async Task CmdModel(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String argument)
        {
            List<List<InlineKeyboardButton>> keyboardRows = new List<List<InlineKeyboardButton>>();
            //List<InlineKeyboardButton> currentRow = new List<InlineKeyboardButton>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            var models = await FoxWorker.GetModels(); // Use the GetModels function here

            if (models.Count == 0)
            {
                throw new Exception("No models available.");
            }

            keyboardRows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("Default", "/model default") });

            foreach (var model in models)
            {
                string modelName = model.Key;
                int workerCount = model.Value.Count; // Assuming you want the count of workers per model

                var buttonLabel = $"{modelName} ({workerCount})";
                var buttonData = $"/model {modelName}"; // Or any unique data you need to pass

                if (modelName == settings.model)
                    buttonLabel += " ✅";

                var button = InlineKeyboardButton.WithCallbackData(buttonLabel, buttonData);

                // Add each button as a new row for a single column layout
                keyboardRows.Add(new List<InlineKeyboardButton> { button });
            }

            keyboardRows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("❌ Cancel", "/model cancel") });

            var inlineKeyboard = new InlineKeyboardMarkup(keyboardRows);

            // Send the message with the inline keyboard
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "Select a model:",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Run a standard txt2img generation.")]
        [CommandArguments("[<prompt>]")]
        private static async Task CmdGenerate(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String argument)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (!string.IsNullOrEmpty(argument))
            {
                settings.prompt = argument; //.Replace("\n", ", ");
                await settings.Save();
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

            if (await FoxQueue.GetCount(user.UID) >= q_limit)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌Maximum of {q_limit} queued requests per user.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            if (await FoxWorker.GetWorkersForModel(settings.model) is null)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.model}).\r\n\r\nPlease try again later or select a different /model.",
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

            FoxWorker.Ping();

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
                settings.negative_prompt = argument; //.Replace("\n", ", ");
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

                settings.prompt = argument; //.Replace("\n", ", ");

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

        [CommandDescription("Set the seed value for the next generation. Default: -1 (random)")]
        [CommandArguments("[<value>]")]
        private static async Task CmdSetSeed(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {

            int seed = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Current Seed: " + settings.seed,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (!int.TryParse(argument, out seed))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌You must provide a numeric value.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                return;
            }

            settings.seed = seed;

            await settings.Save();

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"✅ Seed set to {seed}.",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
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

            if (steps > 20 && !user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Only premium users can exceed 20 steps.\r\n\r\nConsider making a donation: /donate",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );

                if (settings.steps > 20)
                {
                    settings.steps = 20;

                    await settings.Save();
                }
                return;
            }
            else if (steps < 1 || (steps > 40 && !user.CheckAccessLevel(AccessLevel.ADMIN)))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Value must be above 1 and below 40.",
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
                      $"🧠Model: {settings.model}\r\n" +
                      $"🌱Seed: {settings.seed}\r\n",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Cancel all pending requests.")]
        [CommandArguments("")]
        private static async Task CmdCancel(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken, FoxUser user, String? argument)
        {
            int count = 0;
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            var cancelMsg = await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"⏳ Cancelling...",
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );

            List<ulong> pendingIds = new List<ulong>();

            using (var cmd2 = new MySqlCommand("SELECT id,msg_id,tele_chatid FROM queue WHERE uid = @id AND status = 'PENDING' OR status = 'ERROR'", SQL))
            {
                cmd2.Parameters.AddWithValue("id", user.UID);
                using var reader = await cmd2.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    ulong q_id = reader.GetUInt64("id");
                    long chat_id = reader.GetInt64("tele_chatid");
                    int msg_id = reader.GetInt32("msg_id");

                    try
                    {
                        await botClient.EditMessageTextAsync(
                            chatId: chat_id,
                            messageId: msg_id,
                            text: "❌ Cancelled."
                        );
                    } catch
                    {
                        //Don't care about this failure.
                    }

                    pendingIds.Add(q_id);

                    count++;
                }
            }

            if (pendingIds.Count > 0)
            {
                // Create a comma-separated list of IDs for the SQL query
                var idsString = string.Join(",", pendingIds);
                using (var cmd3 = new MySqlCommand($"UPDATE queue SET status = 'CANCELLED' WHERE id IN ({idsString})", SQL))
                {
                    await cmd3.ExecuteNonQueryAsync();
                }
            }


            List<ulong> processingIds = new List<ulong>();

            using (var cmd2 = new MySqlCommand("SELECT id,worker,msg_id,tele_chatid FROM queue WHERE uid = @id AND status = 'PROCESSING'", SQL))
            {
                cmd2.Parameters.AddWithValue("id", user.UID);
                using var r = await cmd2.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    int? worker_id = (r["worker"] is DBNull) ? null : r.GetInt32("worker");
                    ulong q_id = r.GetUInt64("id");
                    long chat_id = r.GetInt64("tele_chatid");
                    int msg_id = r.GetInt32("msg_id");

                    if (worker_id is not null && FoxWorker.CancelIfUserMatches(worker_id.Value, user.UID))
                        processingIds.Add(q_id);

                    try {
                        await botClient.EditMessageTextAsync(
                            chatId: chat_id,
                            messageId: msg_id,
                            text: "⏳ Cancelling..."
                        );

                    }
                    catch
                    {
                        //Don't care about this failure.
                    }

                    count++;
                }
            }

            if (processingIds.Count > 0)
            {
                // Create a comma-separated list of IDs for the SQL query
                var idsString = string.Join(",", processingIds);
                using (var cmd3 = new MySqlCommand($"UPDATE queue SET status = 'CANCELLED' WHERE id IN ({idsString})", SQL))
                {
                    await cmd3.ExecuteNonQueryAsync();
                }
            }

            await botClient.EditMessageTextAsync(
                chatId: message.Chat.Id,
                text: $"✅ Cancelled {count} items.",
                messageId: cancelMsg.MessageId,
                cancellationToken: cancellationToken
            );
        }

        [CommandDescription("Change the size of the output, e.g. /setsize 768x768")]
        [CommandArguments("<width>x<height>")]
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

            if (width < 512 || height < 512)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Dimenion should be at least 512 pixels.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            if ((width > 1024 || height > 1024) && !user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Only premium users can exceed 1024 pixels in any direction.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            } else if (width > 1280 || height > 1280 && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Dimension cannot be greater than 1280.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
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

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: msgString,
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken
            );;
        }
    }
}
