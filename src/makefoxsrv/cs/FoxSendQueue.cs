using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;
using WTelegram;
using makefoxsrv;
using TL;
using System.Security.Policy;
using SixLabors.ImageSharp.PixelFormats;
using System.Drawing.Imaging;
using Newtonsoft.Json;

//This isn't properly implemented yet; we really need a way to handle Telegram's rate limits by using a proper message queue.
// For now we just push the message to the user and hope for the best.

namespace makefoxsrv
{
    internal class FoxSendQueue
    {
        public static async Task<FoxSendQueue?> Pop()
        {
            return null;
        }

        public enum SafetyCheckResult
        {
            None,
            FalsePositive,
            Unsafe
        }

        public static async Task<SafetyCheckResult> CheckPromptSafetyAsync(
            FoxEmbedding candidate,
            double threshold = 0.8,
            int limit = 1000)
        {
            double avgFalsePos = 0.0;
            double avgUnsafe = 0.0;

            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            // Second: check against known UNSAFE
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT AVG(1 - VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec))) AS avg_similarity
                    FROM (
                        SELECT embedding
                        FROM queue_embeddings
                        WHERE type = 'USER_PROMPT'
                          AND safety_status = 'UNSAFE'
                        ORDER BY VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec)) ASC
                        LIMIT @limit
                    ) sub;";
                cmd.Parameters.AddWithValue("@vec", candidate.ToString());
                cmd.Parameters.AddWithValue("@limit", limit);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    avgUnsafe = Convert.ToDouble(result);
            }

            if (avgUnsafe >= threshold)
                return SafetyCheckResult.Unsafe;


            // First: check against known FALSE_POSITIVES
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT AVG(1 - VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec))) AS avg_similarity
                    FROM (
                        SELECT embedding
                        FROM queue_embeddings
                        WHERE type = 'USER_PROMPT'
                          AND safety_status = 'FALSE_POSITIVE'
                        ORDER BY VEC_DISTANCE_COSINE(embedding, VEC_FromText(@vec)) ASC
                        LIMIT @limit
                    ) sub;";
                cmd.Parameters.AddWithValue("@vec", candidate.ToString());
                cmd.Parameters.AddWithValue("@limit", limit);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    avgFalsePos = Convert.ToDouble(result);
            }

            if (avgFalsePos >= threshold)
                return SafetyCheckResult.FalsePositive;

            return SafetyCheckResult.None;
        }

        private static async Task<(FoxEmbedding promptEmbedding, FoxEmbedding tagsEmbedding)> DoEmbeddingAndStoreAsync(FoxQueue q)
        {
            try
            {
                // So that both entries have the same time
                var now = DateTime.Now;

                // Kick off the prompt embedding right away
                var promptTask = FoxEmbedding.CreateAsync(q.Settings.Prompt);

                // Meanwhile get the image + tags
                var image = await q.GetOutputImage();
                var tags = await image.GetImageTagsAsync();

                // Kick off the tags embedding at the same time
                var tagsTask = FoxEmbedding.CreateAsync(string.Join(", ", tags.Keys));

                // Wait for both to finish in parallel
                var promptEmbedding = await promptTask;

                using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                await conn.OpenAsync();

                // Insert prompt embedding
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO queue_embeddings (qid, type, embedding, date_generated)
                        VALUES (@qid, 'USER_PROMPT', VEC_FromText(@vec), @now)";

                    cmd.Parameters.AddWithValue("@qid", q.ID);
                    cmd.Parameters.AddWithValue("@vec", promptEmbedding.ToString());
                    cmd.Parameters.AddWithValue("@now", now);

                    await cmd.ExecuteNonQueryAsync();
                }

                var tagsEmbedding = await tagsTask;

                // Insert tags embedding
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO queue_embeddings (qid, type, embedding, date_generated)
                        VALUES (@qid, 'PREDICTED_TAGS', VEC_FromText(@vec), @now)";

                    cmd.Parameters.AddWithValue("@qid", q.ID);
                    cmd.Parameters.AddWithValue("@vec", tagsEmbedding.ToString());
                    cmd.Parameters.AddWithValue("@now", now);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Log similarity for debugging
                var similarity = FoxEmbedding.CosineSimilarity(promptEmbedding, tagsEmbedding);

                //FoxLog.WriteLine($"{q.ID}: User prompt: {q.Settings.Prompt}");
                //FoxLog.WriteLine($"{q.ID}: Image tags : {string.Join(", ", tags.Keys)}");
                //FoxLog.WriteLine($"{q.ID}: Cosine similarity: {similarity}");

                return (promptEmbedding, tagsEmbedding);
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Embedding failed for {q.ID}: {ex.Message}");
                throw;
            }
        }


        public static async Task Send(FoxQueue q)
        {
            FoxLog.WriteLine($"Sending task {q.ID} to Telegram...", LogLevel.DEBUG);

            try
            {
                if (q.status == FoxQueue.QueueStatus.CANCELLED || q.stopToken.IsCancellationRequested)
                    throw new Exception("Tried to send cancelled task {q.ID}.");

                var OutputImage = await q.GetOutputImage();


                if (OutputImage is not null && OutputImage.Image is not null)
                {
                    var t = q.Telegram;

                    if (t is null)
                        throw new Exception("Telegram object not initalized!");

                    // If the user already has queue items that are sending, we need to add a brief delay so they don't overlap.
                    // Make sure we don't count this one.

                    var sendingItemsCount = FoxQueue.Cache.FindAll(x => x.User == q.User && x.ID != q.ID && x.status == FoxQueue.QueueStatus.SENDING).Count();

                    if (sendingItemsCount > 0)
                    {
                        FoxLog.WriteLine($"Task {q.ID} - Found {sendingItemsCount} currently sending; adding delay.", LogLevel.DEBUG);

                        await Task.Delay(500 * sendingItemsCount);
                    }

                    if (!FoxContentFilter.ImageTagsSafetyCheck(await OutputImage.GetImageTagsAsync()))
                    {
                        // Start these tasks immediately
                        var embeddingsTask = DoEmbeddingAndStoreAsync(q);
                        var llmModerationTask = FoxContentFilter.CheckUserIntentAsync(q);

                        try {
                            await t.EditMessageAsync(
                                id: q.MessageID,
                                text: $"⏳ Performing moderation safety checks..."
                            );
                        }
                        catch { } //We don't care if editing fails.

                        bool falsePositive = false;

                        // Now await all tasks to finish

                        string? llmReasonStr = null;

                        try
                        {
                            var llmResults = await llmModerationTask;

                            if (!string.IsNullOrEmpty(llmResults.user_message))
                                llmReasonStr = llmResults.user_message;

                            var debugMsg = $"LLM moderation result for {q.User.UID}:{q.ID}: {JsonConvert.SerializeObject(llmResults, Formatting.Indented)}";

                            FoxLog.WriteLine(debugMsg);

                            _ = FoxContentFilter.SendModerationNotification(debugMsg);

                            if (!llmResults.violation && llmResults.confidence > 5 && llmResults.intent != FoxContentFilter.ModerationIntent.Deliberate)
                                falsePositive = true;
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex, "Error during LLM-based moderation of {q.User.UID}:{q.ID}: " + ex.Message);
                        }

                        if (!falsePositive)
                        {

                            // Record in the background.
                            _ = FoxContentFilter.RecordViolationsAsync(q.ID, new List<ulong> { 0 });

                            FoxLog.WriteLine($"Task {q.ID} - Image failed safety check; cancelling.");
                            await q.SetCancelled(true);

                            OutputImage.Flagged = true;
                            await OutputImage.Save();

                            try
                            {
                                await FoxTelegram.Client.DeleteMessages(t.Peer, new int[] { q.MessageID });
                            }
                            catch { } //We don't care if deleting fails.

                            var sb = new StringBuilder();
                            sb.AppendLine("❌ Image was detected as prohibited content and has been removed.");
                            if (!string.IsNullOrEmpty(llmReasonStr))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"⚠️ {llmReasonStr}");
                            }
                            sb.AppendLine();
                            sb.AppendLine("If you believe this was in error, please contact support at @makefoxhelpbot.");
                            sb.AppendLine();
                            sb.AppendLine("You can review our rules and content policy by typing /start");

                            try
                            {
                                if (q.ReplyMessageID is not null && q.Telegram is not null)
                                {
                                    await q.Telegram.SendMessageAsync(
                                        replyToMessageId: q.ReplyMessageID ?? 0,
                                        replyToTopicId: q.ReplyTopicID ?? 0,
                                        text: sb.ToString()
                                    );
                                }
                            }
                            catch { } //We don't care if editing fails.

                            return;
                        }
                    }

                    try
                    {
                        await t.EditMessageAsync(
                            id: q.MessageID,
                            text: $"⏳ Uploading..."
                        );
                    }
                    catch (WTelegram.WTException ex)
                    {
                        //If we can't edit, we probably hit a rate limit with this user.

                        if (ex is RpcException rex)
                        {
                            if ((ex.Message.EndsWith("_WAIT_X") || ex.Message.EndsWith("_DELAY_X")))
                            {
                                await q.SetError(ex);

                                return;
                            }
                        }
                        else
                        {
                            //We don't usually care if editing fails.
                        }
                    }
                    catch
                    {
                        //We don't usually care if editing fails.
                    }

                    var oldMessageID = q.MessageID;

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
                                    new TL.KeyboardButtonCallback { text = "🔎 Show Details", data = System.Text.Encoding.ASCII.GetBytes("/info " + q.ID)},
                                }
                            }
                        }
                    };

                    bool addWatermark = !(q.User.CheckAccessLevel(AccessLevel.PREMIUM));

                    using var jpegImage = ConvertImageToJpeg(new MemoryStream(OutputImage.Image), 80, 1280, addWatermark);

                    var inputImage = await FoxTelegram.Client.UploadFileAsync(jpegImage, $"{FoxTelegram.Client.User.username}_smimage_{q.ID}.jpg");

                    //var msg = await FoxTelegram.Client.SendMediaAsync(t.Peer, "", inputImage, null, (int)q.reply_msg);

                    System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateQueued ?? DateTime.UnixEpoch);
                    System.TimeSpan GPUTime = await q.GetGPUTime();
                    string messageText = $"✅ Complete! (Took {diffResult.ToPrettyFormat()} - GPU: {GPUTime.ToPrettyFormat()}";

                    if (OutputImage.Width > 1280 || OutputImage.Height > 1280)
                    {
                        messageText += $"\n\n⚠️ Image dimensions exceed Telegram's maximum image preview size.  For best quality, click below to download the full resoluton file.";
                    }

                    Regex regex = new Regex(@"<lora:[^>:]+(:\d+)?>", RegexOptions.IgnoreCase);

                    if (q.RegionalPrompting && (regex.IsMatch(q.Settings.Prompt ?? "") || regex.IsMatch(q.Settings.NegativePrompt ?? "")))
                    {
                        messageText += $"\n\n⚠️ LoRAs are known to behave strangely when using regional prompting.  If your image appears strange or corrupted, remove LoRAs from your prompts and try again.";
                    }

                    if (t.Chat is null && !q.User.CheckAccessLevel(AccessLevel.PREMIUM)) {
                        int recentCount = await FoxQueue.GetRecentCount(q.User, TimeSpan.FromHours(24));

                        if (recentCount >= 30 && (recentCount % 10 == 0)) // Check if recentCount is a multiple of 10
                        {
                            messageText += $"\n\n🤔 You've generated {recentCount} images in the last 24 hours.\r\n\r\nIf you're enjoying the bot, please consider purchasing a premium /membership to support our service and unlock more features.\r\n\r\nWe depend solely on financial support from our users to keep this service running, and even a little bit really helps.\r\n\r\nThank you for your consideration!";
                            FoxLog.WriteLine($"Nagging user {q.User.Username ?? q.User.UID.ToString()}");
                        }
                    }

                    var msg = await t.SendMessageAsync(
                        media: new InputMediaUploadedPhoto() { file = inputImage },
                        text: (t.Chat is not null ? messageText : null),
                        replyInlineMarkup: (t.Chat is not null ? inlineKeyboardButtons : null),
                        replyToMessageId: q.ReplyMessageID ?? 0,
                        replyToTopicId: q.ReplyTopicID ?? 0
                        );

                    await q.SetStatus(FoxQueue.QueueStatus.FINISHED, msg.ID);

                    if (t.Chat is null) // Only offer the buttons in private chats, not groups.
                    {
                        await t.SendMessageAsync(
                            text: messageText,
                            replyInlineMarkup: inlineKeyboardButtons
                        );
                    }

                    /*
                    var msg = await t.SendMessageAsync(
                        media: new InputMediaUploadedPhoto() { file = inputImage },
                        text: messageText,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessageId: q.ReplyMessageID ?? 0
                        );

                    await q.SetStatus(FoxQueue.QueueStatus.FINISHED, msg.ID); */

                    try
                    {
                        await t.DeleteMessage(oldMessageID);
                    }
                    catch { } // We don't care if editing fails
                }
                else
                {
                    FoxLog.WriteLine("SendQueue {q.ID} to {q.User?.UID}: Unexpected error; output image was null.");
                    await q.SetCancelled(true);

                    try
                    {
                        if (q.ReplyMessageID is not null && q.Telegram is not null)
                        {
                            await q.Telegram.EditMessageAsync(
                                id: q.ReplyMessageID.Value,
                                text: $"❌ An unexpected error occured.  Please try again."
                            );
                        }

                        
                    }
                    catch { } //We don't care if editing fails.
                }

                //FoxLog.WriteLine("Upload Complete");
                FoxLog.WriteLine($"Done sending task {q.ID} to Telegram.", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                //FoxLog.WriteLine($"Failed to send image {q.ID} to {q.User?.UID} - {ex.Message}\r\n{ex.StackTrace}");
                await q.SetError(ex);
            }
        }

        private static MemoryStream ConvertImageToJpeg(MemoryStream inputImageStream, int quality = 85, uint? maxDimension = 1280, bool addWatermark = true)
        {
            var outputStream = new MemoryStream();

            using (Image<Rgba32> image = Image.Load<Rgba32>(inputImageStream))
            {
                if (maxDimension is not null)
                {
                    (uint newWidth, uint newHeight) = FoxImage.CalculateLimitedDimensions((uint)image.Width, (uint)image.Height, maxDimension.Value);

                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size((int)newWidth, (int)newHeight),
                        Mode = ResizeMode.Max
                    }));
                }

                using var image2 = image.Clone();

                image2.SaveAsJpeg(outputStream, new JpegEncoder { Quality = quality });
                outputStream.Position = 0;

                return outputStream;
            }
        }
    }
} 