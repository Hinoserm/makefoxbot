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
using System.Collections;
using System.Text.RegularExpressions;

// Functions and commands specific to generating images

namespace makefoxsrv
{
    internal class FoxGenerate
    {
        private static bool DetectRegionalPrompting(string input)
        {
            // Define the regular expression pattern to match the words
            string pattern = @"\b(ADDCOL|ADDROW|ADDCOMM|ADDBASE)\b";

            // Create a Regex object with the pattern
            Regex regex = new Regex(pattern);

            // Return true if any matches are found, otherwise false
            return regex.IsMatch(input);
        }

        public static async Task HandleCmdGenerate(FoxTelegram t, Message message, FoxUser user, String? argument, FoxQueue.QueueType imgType = FoxQueue.QueueType.TXT2IMG)
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

            if (imgType == FoxQueue.QueueType.IMG2IMG)
            {
                var loadedImg = await FoxImage.SaveImageFromReply(t, message);

                if (loadedImg is not null)
                {
                    settings.selected_image = loadedImg.ID;
                    await settings.Save();
                } else if (settings.selected_image > 0) {
                    loadedImg = await FoxImage.Load(settings.selected_image);
                }

                if (loadedImg is null)
                {
                    if (t.Chat is not null)
                    {
                        await t.SendMessageAsync(
                            text: $"❌ Reply to this message with an image, or send an image with @{FoxTelegram.Client.User.username} in the message, then run /img2img again.",
                            replyToMessageId: message.ID
                        );
                    }
                    else
                    {
                        await t.SendMessageAsync(
                            text: "❌You must upload or /select an image first to use img2img functions.",
                            replyToMessageId: message.ID
                        );
                    }

                    return;
                }
            }

            if (String.IsNullOrEmpty(settings.prompt))
            {
                await t.SendMessageAsync(
                    text: "❌You must specify a prompt!  Please seek /help",
                    replyToMessageId: message.ID
                );

                return;
            }

            await FoxGenerate.Generate(t, settings, message.ID, user, imgType);
        }


        public static async Task Generate(FoxTelegram t, FoxUserSettings settings, int messageId, FoxUser user, FoxQueue.QueueType imgType = FoxQueue.QueueType.TXT2IMG, bool enhanced = false, FoxQueue? originalTask = null)
        {


            if (originalTask is null)
                settings.regionalPrompting = DetectRegionalPrompting(settings.prompt ?? "") || DetectRegionalPrompting(settings.negative_prompt ?? "");
            else
                settings.regionalPrompting = originalTask.Settings.regionalPrompting;

            if (settings.regionalPrompting && !user.CheckAccessLevel(AccessLevel.PREMIUM)) {
                await t.SendMessageAsync(
                    text: "❌ Regional prompting is a premium feature.\n\nPlease consider a paid /membership",
                    replyToMessageId: messageId
                );

                return;
            }

            if (user.GetAccessLevel() < AccessLevel.ADMIN)
            {
                int q_limit = (user.GetAccessLevel() >= AccessLevel.PREMIUM) ? 3 : 1;

                if (await FoxQueue.GetCount(user) >= q_limit)
                {
                    var plural = q_limit == 1 ? "" : "s";

                    await t.SendMessageAsync(
                        text: $"❌ Maximum of {q_limit} queued request{plural}.",
                        replyToMessageId: messageId
                    );

                    return;
                }
            }

            var model = FoxModel.GetModelByName(settings.model);

            if (model is null || model.GetWorkersRunningModel().Count < 1)
            {
                await t.SendMessageAsync(
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.model}).  This can happen if the server was recently restarted or if a model was uninstalled.\r\n\r\nPlease try again in a moment or select a different /model.",
                    replyToMessageId: messageId
                );

                return;
            }

            if (FoxQueue.CheckWorkerAvailability(settings) is null)
            {
                await t.SendMessageAsync(
                    text: "❌ No workers are available to process this task.\n\nPlease reduce your /size, select a different /model, or try again later.",
                    replyToMessageId: messageId
                );

                return;
            }

            // Check if the user is premium
            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM);

            // Get the total count and recently generated count for the user
            //int totalCount = await FoxQueue.GetTotalCount(user);
            //int recentCount = await FoxQueue.GetRecentCount(user, TimeSpan.FromHours(3));

            TimeSpan? delay = null;

            Message? waitMsg;

            // Apply delay for non-premium users after 100 generations
            //if (false && (!isPremium && totalCount > 100) || normalizedComplexity > 1.0)
            //{

            //    double complexityDelay = Math.Round(0.2 + normalizedComplexity * (5.0 - 0.2), 1);

            //    // Calculate additional delay based on recent count
            //    double delaySeconds = Math.Round(Math.Min(recentCount * complexityDelay, 30), 1);

            //    if (normalizedComplexity > 1.0)
            //        delaySeconds = Math.Max(delaySeconds, 30);

            //    delay = TimeSpan.FromSeconds(delaySeconds);

            //    var msgString = $"⏳ Adding to queue...";

            //    // Nag non-premium users every 5th image or if the delay is substantial
            //    //if (totalCount % 5 == 0 || delaySeconds > 15)
            //    //{
            //    //    msgString += "\n\n✨ Consider a /membership for faster processing and other benefits!";
            //    //}

            //    waitMsg = await t.SendMessageAsync(
            //        text: msgString,
            //        replyToMessageId: message.ID
            //    );

            //    if (normalizedComplexity > 1.0)
            //        FoxLog.WriteLine($"Delaying generation for premium user {user.UID} for {delaySeconds:F2} seconds due to image complexity of {normalizedComplexity}");
            //    else
            //        FoxLog.WriteLine($"Delaying generation for non-premium user {user.UID} for {delaySeconds:F2} seconds ({recentCount} * {complexityDelay}).");
            //}
            //else
            //{

            var inlineKeyboardButtons = new ReplyInlineMarkup()
            {
                rows = new TL.KeyboardButtonRow[] {
                            new TL.KeyboardButtonRow {
                                buttons = new TL.KeyboardButtonCallback[]
                                {
                                    new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes("/cancel") },
                                }
                            }
                        }
            };

            //FoxLog.WriteLine($"{message.ID}: CmdGenerate: Checking next position...");
            (int position, int totalItems) = FoxQueue.GetNextPosition(user, false);

            //FoxLog.WriteLine($"{message.ID}: CmdGenerate: Sending message...");
            waitMsg = await t.SendMessageAsync(
                text: $"⏳ Adding to queue ({position} of {totalItems})...",
                replyToMessageId: messageId,
                replyInlineMarkup: inlineKeyboardButtons
            );

            var q = await FoxQueue.Add(t, user, settings, imgType, waitMsg.ID, messageId, enhanced, originalTask, delay: delay);
            if (q is null)
                throw new Exception("Unable to add item to queue");

            FoxContextManager.Current.Queue = q;

            await FoxQueue.Enqueue(q);
        }
    }
}
