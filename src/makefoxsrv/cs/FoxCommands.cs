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
    internal class FoxCommandHandlerOld
    {
        private static readonly Dictionary<string, Func<FoxTelegram, TL.Message, FoxUser, String?, Task>> CommandMap = new Dictionary<string, Func<FoxTelegram, TL.Message, FoxUser, String?, Task>>
        {
            { "/pizza",       CmdTest },
            { "/test",        CmdTest },
            //--------------- -----------------
            { "/setscale",    CmdSetCFG },
            { "/setcfg",      CmdSetCFG },
            { "/cfg",         CmdSetCFG },
            //--------------- -----------------
            { "/setsize",     CmdSetSize },
            { "/size",        CmdSetSize },
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
            { "/donate",      CmdDonate },
            { "/membership",  CmdDonate },
            { "/broadcast",   CmdBroadcast },
            //--------------- -----------------
            { "/info",        CmdInfo },
            { "/privacy",     CmdPrivacy },
            { "/admin",       CmdAdmin },
            //--------------- -----------------
            { "/styles",      CmdStyles },
            //--------------- -----------------
            { "/stickerify",  CmdStickerify },
            { "/request",     CmdRequest },
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

            var newResult = await FoxCommandHandler.Dispatch(t, message, message.message);

            if (newResult)
                return;

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
                    if (!fUser.CheckAccessLevel(AccessLevel.ADMIN))
                    {
                        await t.SendMessageAsync(
                            text: "❌ Commands are not permitted in this topic.",
                                replyToMessage: message
                            );

                        return;
                    }
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
            var methodInfo = typeof(FoxCommandHandlerOld).GetMethod(commandName, BindingFlags.NonPublic | BindingFlags.Static);
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
                case "#archive":
                    await FoxAdmin.HandleRunArchiver(t, message, commandArgs);
                    break;;
                case "#leave":
                    await FoxAdmin.HandleLeaveGroup(t, message, commandArgs);
                    break;
                case "#uncache":
                    await FoxAdmin.HandleUncache(t, message, commandArgs);
                    break;
                case "#showgroups":
                case "#groups":
                    await FoxAdmin.HandleShowGroups(t, message, commandArgs);
                    break;
                case "#rotate":
                    await FoxLog.LogRotator.Rotate();
                    break;
                case "#forward":
                    await FoxAdmin.HandleForward(t, message, commandArgs);
                    break;
                case "#download":
                    await FoxCivitaiCommands.AdminCmdDownloadRequests(t, message, user, commandArgs);
                    break;
                default:
                    await t.SendMessageAsync(
                        text: "❌ Unknown command.  Use one of these:\r\n  #uncache, #ban, #unban, #resetterms, #resettos",
                        replyToMessage: message
                    );
                    break;
            }
        }

        //[CommandDescription("Run a standard txt2img generation.")]
        //[CommandArguments("[<prompt>]")]
        private static async Task CmdRequest(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var outMsg = await t.SendMessageAsync(
                text: "⏳ Processing, please wait...",
                replyToMessage: message
            );

            var requestItems = await FoxCivitaiRequests.ParseRequestAsync(message.message, user);

            TL.Message? replyMessage = await t.GetReplyMessage(message);

            if (replyMessage is not null && !String.IsNullOrEmpty(replyMessage.message))
            {
                var replyUser = await FoxUser.GetByTelegramUser(new TL.User() { id = replyMessage.Peer.ID }, false);

                if (replyUser is null)
                    throw new Exception("Unable to find user for reply message");

                var replyRequests = await FoxCivitaiRequests.ParseRequestAsync(replyMessage.message, replyUser);
                if (requestItems.Count > 0)
                    requestItems.AddRange(replyRequests);
            }

            if (requestItems.Count == 0)
            {
                await t.EditMessageAsync(
                    id: outMsg.ID,
                    text: "❌ Error: No valid Civitai models found in the message or CivitAI API error."
                );
                return;
            }

            var allowedTypes = new[]
            {
                FoxCivitai.CivitaiAssetType.LORA,
                // FoxCivitai.CivitaiAssetType.Embedding
            };

            var groupedResults = FoxCivitaiRequests.GroupByType(requestItems);

            var sb = new StringBuilder();

            sb.AppendLine("🦊 I will attempt to automatically download your requests from CivitAI.  I always automatically download all sub-models and versions of each link.");
            sb.AppendLine();

            // Remove already installed LORAs
            if (groupedResults.TryGetValue(FoxCivitai.CivitaiAssetType.LORA, out var loraRequests) && loraRequests.Count > 0)
            {
                var alreadyInstalled = FoxCivitaiRequests.FetchAlreadyInstalled(loraRequests);

                if (alreadyInstalled.Count > 0)
                {
                    var filtered = loraRequests
                        .Where(x => !alreadyInstalled.Any(y => y.InfoItem == x.InfoItem))
                        .ToList();

                    if (filtered.Count > 0)
                    {
                        groupedResults[FoxCivitai.CivitaiAssetType.LORA] = filtered;
                    }
                    else
                    {
                        groupedResults.Remove(FoxCivitai.CivitaiAssetType.LORA);
                    }

                    sb.AppendLine($"⚠️ You have requested {alreadyInstalled.Count} LORA(s) that are already installed and will not be installed again.");
                    sb.AppendLine();
                }
            }

            // Warn and remove unsupported types
            var unsupportedTypes = groupedResults
                .Where(kvp => !allowedTypes.Contains(kvp.Key) && kvp.Value.Count > 0)
                .ToList(); // Avoid modifying the dictionary while enumerating

            foreach (var kvp in unsupportedTypes)
            {
                sb.AppendLine($"⚠️ You have requested {kvp.Value.Count} item(s) of type {kvp.Key}, which are not supported and will not be downloaded.");
                sb.AppendLine();
                groupedResults.Remove(kvp.Key);
            }

            // Remove items with missing SHA256, DownloadUrl, or invalid file extension
            var invalidItems = new List<(FoxCivitai.CivitaiAssetType Type, FoxCivitai.CivitaiInfoItem Item)>();

            foreach (var kvp in groupedResults.ToList())
            {
                var validItems = new List<FoxCivitaiRequests.CivitaiRequestItem>();

                foreach (var item in kvp.Value)
                {
                    var primaryFile = item.InfoItem.primaryFile;

                    bool hasHash = !string.IsNullOrWhiteSpace(primaryFile?.SHA256);
                    bool hasUrl = !string.IsNullOrWhiteSpace(primaryFile?.DownloadUrl);

                    if (hasHash && hasUrl)
                        validItems.Add(item);
                    else
                        invalidItems.Add((kvp.Key, item.InfoItem));
                }

                if (validItems.Count > 0)
                    groupedResults[kvp.Key] = validItems;
                else
                    groupedResults.Remove(kvp.Key);
            }

            // Warn about dropped invalids
            if (invalidItems.Count > 0)
            {
                var groupedByType = invalidItems
                    .GroupBy(i => i.Type);

                foreach (var group in groupedByType)
                {
                    var type = group.Key;
                    var items = group.Select(x => x.Item)
                        .Where(x => !string.IsNullOrWhiteSpace(x.primaryFile?.Name))
                        .Select(x => System.IO.Path.GetFileNameWithoutExtension(x.primaryFile?.Name))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var displayNames = items.Take(5).ToList();
                    var remaining = items.Count - displayNames.Count;

                    var line = $"⚠️ Skipped {group.Count()} item(s) of type {type} due to missing or unsupported files: {string.Join(", ", displayNames)}";

                    if (remaining > 0)
                    {
                        line += $" (and {remaining} others)";
                    }

                    sb.AppendLine(line);
                    sb.AppendLine();
                }
            }

            // Check if any allowed types are present
            bool hasAllowedItems = groupedResults.Keys.Any(k => allowedTypes.Contains(k));

            if (!hasAllowedItems)
            {
                var allowedList = string.Join(", ", allowedTypes.Select(t => t.ToString()));

                sb.Append($"❌ Error: No downloadable items found in the message.");
                sb.AppendLine($" You may only request the following types using this automated system: {allowedList}.");

                await t.EditMessageAsync(
                    text: sb.ToString(),
                    id: outMsg.ID
                );

                return;
            }

            var existingRequests = await FoxCivitaiRequests.FetchAllRequestsAsync();

            // Exclude already requested items

            var existingHashes = existingRequests
                .Where(x => !string.IsNullOrWhiteSpace(x.InfoItem.primaryFile?.SHA256))
                .Select(x => x.InfoItem.primaryFile?.SHA256!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Flatten everything into one list and find conflicts
            var conflicting = groupedResults
                .SelectMany(kvp => kvp.Value.Select(item => (Type: kvp.Key, Item: item)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Item.InfoItem.primaryFile?.SHA256) && existingHashes.Contains(x.Item.InfoItem.primaryFile?.SHA256!))
                .ToList();

            // Remove the conflicting items from groupedResults
            foreach (var (type, item) in conflicting)
            {
                groupedResults[type].Remove(item);
                if (groupedResults[type].Count == 0)
                    groupedResults.Remove(type);
            }

            // Report skipped items
            if (conflicting.Count > 0)
            {
                foreach (var group in conflicting.GroupBy(x => x.Type))
                {
                    var names = group.Select(x => System.IO.Path.GetFileNameWithoutExtension(x.Item.InfoItem.primaryFile?.Name ?? ""))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var display = names.Take(5).ToList();
                    var remaining = names.Count - display.Count;

                    var line = $"⚠️ Skipped {group.Count()} {group.Key} item(s) already requested (pending approval): {string.Join(", ", display)}";
                    if (remaining > 0)
                        line += $" (and {remaining} others)";

                    sb.AppendLine(line);
                    sb.AppendLine();
                }
            }

            // Check and rename conflicting filenames

            //foreach (var (type, items) in groupedResults)
            //{
            //    var renames = FoxCivitaiRequests.EnsureUniqueFilenames(items);

            //    if (renames.Count > 0)
            //    {
            //        sb.AppendLine($"⚠️ The following {type} request(s) had conflicting filenames and were renamed:");

            //        foreach (var (original, updated) in renames)
            //            sb.AppendLine($"{original} → {updated}");

            //        sb.AppendLine();
            //    }
            //}

            // Process allowed types
            foreach (var type in allowedTypes)
            {
                if (groupedResults.TryGetValue(type, out var items) && items.Count > 0)
                {
                    var names = items
                        .Where(x => !string.IsNullOrWhiteSpace(x.InfoItem.primaryFile?.Name))
                        .Select(x => System.IO.Path.GetFileNameWithoutExtension(x.InfoItem.primaryFile?.Name))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var displayNames = names.Take(5).ToList();
                    var remaining = names.Count - displayNames.Count;

                    var line = $"ℹ️ You are requesting {items.Count} {type}s to be installed: {string.Join(", ", displayNames)}";

                    if (remaining > 0)
                    {
                        line += $" (and {remaining} others)";
                    }

                    sb.AppendLine(line);
                    sb.AppendLine();
                }
            }

            if (groupedResults.Count > 0)
                sb.AppendLine("✅ Your requests will be installed once approved by staff.");
            else
                sb.AppendLine("❌ Nothing to do.  Aborting.");

            await t.EditMessageAsync(
                text: sb.ToString(),
                id: outMsg.ID
            );

            foreach (var (type, items) in groupedResults)
            {
                await FoxCivitaiRequests.InsertRequestItemsAsync(items);
            }
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

            // Add sale button
            //buttonRows.Add(new TL.KeyboardButtonRow
            //{
            //    buttons = new TL.KeyboardButtonCallback[]
            //    {
            //        new() { text = "✨💰 💳 $50 (356 Days) SALE 💰✨", data = System.Text.Encoding.UTF8.GetBytes("/donate promo50") }
            //    }
            //});

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonUrl[]
                {
                    new() { text = "🔗 Pay in Web Browser", url = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?id={pSession.UUID}" }
                }
            });

            // Add Stars button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "⭐ Use Telegram Stars ⭐", data = System.Text.Encoding.UTF8.GetBytes("/donate stars") }
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
        public static async Task CmdHelp(FoxTelegram t, Message message, FoxUser user, String? argument)
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

                //await commands.CmdAdminInfo.CmdInfo(t, user, message, argument);

                return;
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

                if (user.CheckAccessLevel(AccessLevel.ADMIN))
                {
                    sb.AppendLine();
                    sb.AppendLine("Cache Info:");
                    sb.AppendLine($"  Queue: {FoxQueue.Cache.Count()}");
                    sb.AppendLine($"  Users: {FoxUser.CacheCount()}");
                    sb.AppendLine($"  Images: {FoxImage.CacheCount()}");
                    sb.AppendLine($"  Embeddings: {FoxEmbedding.CacheCount()}");
                }

                sb.AppendLine("\nUser Info:\n");
            }


            sb.AppendLine(await FoxMessages.BuildUserInfoString(selectedUser));


            // Only show global stats if no user was specified
            if (string.IsNullOrEmpty(argument))
            {
                (ulong imageCount, ulong imageBytes) = await FoxImage.GetImageStatsAsync();
                long userCount = 0;
                DateOnly oldestImage = DateOnly.FromDateTime(DateTime.Now);

                sb.AppendLine("Global Stats:\n");

                using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await connection.OpenAsync();

                    MySqlCommand sqlcmd;

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

            var showProfileButton = true;

            while (true) {
                List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

                if (selectedUser is not null)
                {


                    if (showProfileButton && selectedUser.Telegram is not null)
                    {
                        buttonRows.Add(new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonBase[]
                            {
                                new KeyboardButtonUrl() { text = "🔗 Image Viewer", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={selectedUser.UID}" },
                                new InputKeyboardButtonUserProfile() { text = "View Profile", user_id = selectedUser.Telegram.User }
                            }
                        });
                    }
                    else
                    {
                        buttonRows.Add(new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonBase[]
                            {
                                new KeyboardButtonUrl() { text = "🔗 Image Viewer", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={selectedUser.UID}" }
                            }
                        });
                    }
                }

                try
                {
                    await t.SendMessageAsync(
                        text: sb.ToString(),
                        replyToMessage: message,
                        replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                    );
                }
                catch (Exception ex)
                {
                    // Probably hit an error about the profile button
                    
                    FoxLog.LogException(ex);

                    if (!showProfileButton)
                        throw; // We already tried without the profile button, so give up.

                    showProfileButton = false;
                    continue;
                }

                break; // Exit the loop if successful
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
            } else if ((width * height) > (2048*2048) && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Total image pixel count cannot be greater than 2048x2048",
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
