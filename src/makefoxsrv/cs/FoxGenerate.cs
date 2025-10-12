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
using System.Reflection.Metadata.Ecma335;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

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
                settings.Prompt = argument; //.Replace("\n", ", ");
                await settings.Save();
            }

            if (user.DateTermsAccepted is null)
            {
                await FoxMessages.SendTerms(t, user, message);

                return; // User must agree to the terms before they can use this command.
            }

            var floodWait = await user.GetFloodWait();

            if (t.Chat is null && floodWait is not null && floodWait >= DateTime.Now)
            {
                TimeSpan waitTime = floodWait.Value - DateTime.Now;

                await t.SendMessageAsync(
                    text: $"❌ Telegram is currently reporting that you've exceeded the image upload rate limit.  This rate limit is outside of our control.\r\n\r\nPlease try again in {waitTime.ToPrettyFormat()}.",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (imgType == FoxQueue.QueueType.IMG2IMG)
            {
                var loadedImg = await FoxImage.SaveImageFromReply(t, message);

                if (loadedImg is not null)
                {
                    settings.SelectedImage = loadedImg.ID;
                    await settings.Save();
                } else if (settings.SelectedImage > 0) {
                    loadedImg = await FoxImage.Load(settings.SelectedImage);
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

            if (String.IsNullOrEmpty(settings.Prompt))
            {
                await t.SendMessageAsync(
                    text: "❌You must specify a prompt!  Please seek /help",
                    replyToMessage: message
                );

                return;
            }

            await FoxGenerate.Generate(t, settings, message, user, imgType);
        }

        private static async Task<bool> Check(FoxTelegram t, FoxUser user, Message? replyToMessage, FoxUserSettings settings)
        {
            //if (settings.regionalPrompting)
            //    throw new Exception("Regional prompting is currently unavailable due to a software issue.");

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (settings.regionalPrompting && !isPremium)
            {
                await t.SendMessageAsync(
                    text: "❌ Regional prompting is a premium feature.\n\nPlease consider a paid /membership",
                    replyToMessage: replyToMessage
                );

                return false;
            }

            if (user.GetAccessLevel() < AccessLevel.ADMIN)
            {
                int q_limit = isPremium ? 3 : 1;

                if (await FoxQueue.GetCount(user) >= q_limit)
                {
                    var plural = q_limit == 1 ? "" : "s";

                    await t.SendMessageAsync(
                        text: $"❌ Maximum of {q_limit} queued request{plural}.",
                        replyToMessage: replyToMessage
                    );

                    return false;
                }
            }

            var model = FoxModel.GetModelByName(settings.ModelName);

            if (model is null || model.GetWorkersRunningModel().Count < 1)
            {
                await t.SendMessageAsync(
                    text: $"❌ There are no workers available to handle your currently selected model ({settings.ModelName}).  This can happen if the server was recently restarted or if a model was uninstalled.\r\n\r\nPlease try again in a moment or select a different /model.",
                    replyToMessage: replyToMessage
                );

                return false;
            }

            if (FoxQueue.CheckWorkerAvailability(settings) is null)
            {
                await t.SendMessageAsync(
                    text: "❌ No workers are available to process this task.\n\nPlease reduce your /size, select a different /model, or try again later.",
                    replyToMessage: replyToMessage
                );

                return false;
            }

            if (!isPremium)
            {

                var modelAllowed = await model.IsUserAllowed(user);

                if (!modelAllowed)
                {
                    var dailyLimit = modelAllowed.dailyLimit;
                    var weeklyLimit = modelAllowed.weeklyLimit;

                    var reason = modelAllowed.Reason;
                    string message = "❌ The selected model is not available right now.  Please try a different /model.";

                    if (reason.HasFlag(FoxModel.DenyReason.RestrictedModel))
                    {
                        message = "❌ This model is currently restricted and cannot be used.  Please try a different /model.";
                    }
                    else if (reason.HasFlag(FoxModel.DenyReason.WeeklyLimitReached))
                    {
                        message =
                            "❌ You've hit your weekly quota for premium models.\r\n\r\n" +
                            $"ℹ️ Free users are limited to {weeklyLimit} images across all premium models per week. " +
                            "Your quota resets every Monday at midnight Central Time (GMT-6).\r\n\r\n" +
                            "✨ Consider a /membership to unlock additional features, or try a free /model.";
                    }
                    else if (reason.HasFlag(FoxModel.DenyReason.DailyLimitReached))
                    {
                        message =
                            "❌ You've reached today's limit for this model.\r\n\r\n" +
                            $"ℹ️ Each premium model is limited to {dailyLimit} images per day for free users. " +
                            "This limit resets at midnight Central Time (GMT-6).\r\n\r\n" +
                            "✨ Consider a /membership to unlock additional features, or try a free /model.";
                    }

                    await t.SendMessageAsync(
                        text: message,
                        replyToMessage: replyToMessage
                    );

                    FoxLog.WriteLine($"User {user.UID} attempted to use restricted model {model.Name}");

                    return false;
                }
            }

            settings.Prompt = FoxLORAs.NormalizeLoraTags(settings.Prompt ?? "", out var missingLoras);

            if (missingLoras.Count > 0)
            {
                var missingLoraNames = string.Join(", ", missingLoras);
                var suggestions = FoxLORAs.SuggestSimilarLoras(missingLoras);

                var suggestionLines = suggestions.Select(kvp =>
                    $"→ {kvp.Key}: {string.Join(", ", kvp.Value.Select(v => v.Filename))}");

                var suggestionText = suggestionLines.Any()
                    ? "\n\nDid you mean:\n" + string.Join("\n", suggestionLines)
                    : "";

                await t.SendMessageAsync(
                    text: $"❌ The following LORAs are not available: {missingLoraNames}.{suggestionText}",
                    replyToMessage: replyToMessage
                );

                return false;
            }

            return true;
        }

        public static async Task<FoxQueue?> Enhance(FoxTelegram t, FoxUser user, Message replyToMessage, FoxQueue originalTask)
        {
            (uint width, uint height) SnapDimensionsToMultiple((uint width, uint height) dimension, uint multiple = 8)
            {
                uint Snap(uint value) => (value / multiple) * multiple;
                return (Snap(dimension.width), Snap(dimension.height));
            }

            FoxUserSettings settings = originalTask.Settings.Copy();

            if (!await Check(t, user, replyToMessage, settings))
                return null;

            //var msg = await t.SendMessageAsync(
            //    text: "⏳ Upscaling...",
            //    replyToMessage: replyToMessage
            //);

            var srcImage = await originalTask.GetOutputImage();

            if (srcImage is null)
                throw new Exception("Unable to load source image");

            var imgId = srcImage.ID;

            //(uint tgtWidth, uint tgtHeight) = SnapDimensionsToMultiple(FoxImage.CalculateLimitedDimensions((uint)srcImage.Width * 2, (uint)srcImage.Height * 2, 2048), 16);

            //if (srcImage.Width < 2048 && srcImage.Height < 2048)
            //{
            //    var upscaledImage = FoxONNXImageUpscaler.Upscale(srcImage.GetRGBAImage());

            //    if (upscaledImage.Width > tgtWidth || upscaledImage.Height > tgtHeight)
            //    {
            //        upscaledImage.Mutate(x => x.Resize((int)tgtWidth, (int)tgtHeight));
            //    }

            //    var newImage = await FoxImage.Create(upscaledImage, FoxImage.ImageType.INPUT, $"{srcImage.ID}_upscaled.png", originalTask.User.UID);
            //    imgId = newImage.ID;
            //}

            //(settings.Width, settings.Height) = FoxImage.CalculateLimitedDimensions(settings.Width * 2, settings.Height * 2, 2048);

            settings.Seed = -1;
            settings.hires_denoising_strength = 0.33M;
            settings.hires_steps = 15;
            settings.hires_enabled = true;

            (settings.hires_width, settings.hires_height) = SnapDimensionsToMultiple(FoxImage.CalculateLimitedDimensions((uint)srcImage.Width * 2, (uint)srcImage.Height * 2, 2048), 16);

            settings.Width = (uint)srcImage.Width;
            settings.Height = (uint)srcImage.Height;

            //settings.SelectedImage = q.OutputImageID.Value;
            settings.SelectedImage = imgId;

            //uint width = Math.Max(settings.Width, settings.hires_width);
            //uint height = Math.Max(settings.Height, settings.hires_height);

            //(settings.Width, settings.Height) = SnapDimensionsToMultiple(FoxImage.CalculateLimitedDimensions(width, height, 2048), 8);
            //(settings.hires_width, settings.hires_height) = SnapDimensionsToMultiple(FoxImage.CalculateLimitedDimensions(width * 2, height * 2, 2048), 16);

            settings.regionalPrompting = originalTask.RegionalPrompting; //Have to copy this over manually

            return await FoxGenerate.Generate(t, settings, replyToMessage, user, FoxQueue.QueueType.IMG2IMG, true, originalTask);
        }


        public static async Task<FoxQueue?> Generate(FoxTelegram t, FoxUserSettings settings, Message? replyToMessage, FoxUser user, FoxQueue.QueueType imgType = FoxQueue.QueueType.TXT2IMG, bool enhanced = false, FoxQueue? originalTask = null, Message? editMessage = null)
        {
            if (originalTask is null)
                settings.regionalPrompting = DetectRegionalPrompting(settings.Prompt ?? "") || DetectRegionalPrompting(settings.NegativePrompt ?? "");
            else
                settings.regionalPrompting = originalTask.Settings.regionalPrompting;

            if (!await Check(t, user, replyToMessage, settings))
                return null;

            // Check if the user is premium
            //bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM);

            var q = await FoxQueue.Add(t, user, settings, imgType, 0, replyToMessage, enhanced, originalTask, status: FoxQueue.QueueStatus.PAUSED);
            if (q is null)
                throw new Exception("Unable to add item to queue");
            if (q.User is null)
                throw new Exception("Queue item has no user");

            FoxContextManager.Current.Queue = q;

            var safetyPromptCache = await FoxContentFilter.SafetyPromptCache.GetStateAsync(q);

            bool safetyPromptCacheViolated = (safetyPromptCache == FoxContentFilter.SafetyPromptCache.SafetyState.UNSAFE);

            if (safetyPromptCacheViolated)
            {
                var warningMsg = await t.SendMessageAsync(
                    text: "❌ This prompt has previously been flagged as potentially violating our content policy and will not be processed.\r\n\r\nIf you believe this is a mistake, please contact @makefoxhelpbot.",
                    replyToMessage: replyToMessage
                );

                await q.SetCancelled(true);

                return null;
            }

            var violatedRules = FoxContentFilter.CheckPrompt(settings?.Prompt ?? "", settings?.NegativePrompt);

            if (violatedRules is not null && violatedRules.Count() > 0)
            {
                //await FoxContentFilter.RecordViolationsAsync(q.ID, violatedRules.Select(r => r.Id).ToList());

                var inlineKeyboardButtons = new ReplyInlineMarkup()
                {
                    rows = new TL.KeyboardButtonRow[] {
                        new TL.KeyboardButtonRow {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = "⚠️ Continue", data = System.Text.Encoding.ASCII.GetBytes($"/continue {q.ID}") },
                                new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes($"/cancel {q.ID}") },
                            }
                        }
                    }
                };

                StringBuilder msgStr = new StringBuilder();

                msgStr.AppendLine("⚠️ This request has been flagged as potentially violating our content policy.\r\n");
                msgStr.AppendLine("You're responsible for ensuring it complies. Violations (intentional or not) can result in account restrictions or a permanent ban.\r\n");
                msgStr.AppendLine("Please review our <link>(TODO) Content Policy</link> before proceeding.\r\n");
                msgStr.AppendLine("If you choose to continue and our moderators determine the content crosses the line, your account may be suspended without further notice.\r\n");
                msgStr.AppendLine("(This system is in early development; contact @makefoxhelpbot if you have questions or concerns.)");

                var warningMsg = await t.SendMessageAsync(
                    text: msgStr.ToString(),
                    replyToMessage: replyToMessage,
                    replyInlineMarkup: inlineKeyboardButtons
                );

                // Do this to set the message ID, even though it's already paused.
                await q.SetStatus(FoxQueue.QueueStatus.PAUSED, warningMsg.ID);
            }
            else
            {
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

                var messageText = "⏳ Adding to queue";

                if (t.Chat is null)
                    messageText += $" ({position} of {totalItems})...";
                else
                    messageText += "...";

                Message? waitMsg = null;

                if (editMessage is not null)
                {
                    try
                    {
                        waitMsg = await t.EditMessageAsync(
                            id: editMessage.ID,
                            text: messageText,
                            replyInlineMarkup: inlineKeyboardButtons
                        );
                    }
                    catch
                    {
                        editMessage = null;
                    }
                }
                
                if (editMessage is null)
                {
                    waitMsg = await t.SendMessageAsync(
                        text: messageText,
                        replyToMessage: replyToMessage,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }

                await q.SetStatus(FoxQueue.QueueStatus.PENDING, waitMsg!.ID);
                FoxQueue.Enqueue(q);

                return q;
            }

            return null;
        }
    }
}
