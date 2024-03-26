using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using Telegram.Bot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;

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

        public static async Task Enqueue(ITelegramBotClient botClient, FoxQueue q)
        {

            if (q.output_image is not null)
            {

                await q.SetSending();

                try
                {
                    await botClient.EditMessageTextAsync(
                        chatId: q.TelegramChatID,
                        messageId: q.msg_id,
                        text: $"⏳ Uploading..."
                    );
                }
                catch (Exception ex) {

                    FoxLog.WriteLine("Error: " + ex.Message);

                } //We don't care if editing fails.

                string? output_fileid = null;

                try
                {
                    var msg = await botClient.SendPhotoAsync(
                        chatId: q.TelegramChatID,
                        photo: InputFile.FromStream(ConvertImageToJpeg(new MemoryStream(q.output_image.Image))),
                        replyToMessageId: (int)q.reply_msg
                        );

                    output_fileid = msg.Photo.First().FileId;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Bad Request: message to reply not found"))
                    {
                        try
                        {
                            var msg = await botClient.SendPhotoAsync(
                                chatId: q.TelegramChatID,
                                photo: InputFile.FromStream(ConvertImageToJpeg(new MemoryStream(q.output_image.Image)))
                                );
                            output_fileid = msg.Photo.First().FileId;
                        }
                        catch (Exception ex2)
                        {
                            FoxLog.WriteLine("ERROR SENDING !!!!! " + ex2.Message);
                        }
                    }
                    else
                        throw ex;
                }

                try
                {
                    await botClient.DeleteMessageAsync(
                        chatId: q.TelegramChatID,
                        messageId: q.msg_id
                    );
                }
                catch { } // We don't care if editing fails

                if (true || q.TelegramChatID == q.TelegramUserID) // Only offer the buttons in private chats, not groups.
                {
                    var inlineKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("👍", "/vote up " + q.id),
                            InlineKeyboardButton.WithCallbackData("👎", "/vote down " + q.id),
                            InlineKeyboardButton.WithCallbackData("💾", "/download " + q.id),
                            InlineKeyboardButton.WithCallbackData("🎨", "/select " + q.id),
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Show Details", "/info " + q.id),
                        }
                    });

                    System.TimeSpan diffResult = DateTime.Now.Subtract(q.creation_time);
                    System.TimeSpan GPUTime = await q.GetGPUTime();

                    await botClient.SendTextMessageAsync(
                        chatId: q.TelegramChatID,
                        text: $"✅ Complete! (Took {diffResult.ToPrettyFormat()} - GPU: {GPUTime.ToPrettyFormat()}",
                        replyMarkup: inlineKeyboard
                    );
                }

                // new InputFileId(output_fileid)

                await q.Finish();

                bool success = false;
                //while (!success)
                while (false) //Disabled for now.
                {
                    try
                    {
                        // Attempt to run the operation that might fail
                        var u = await FoxTelegramUser.Get(q.TelegramUserID);
                        var c = (q.TelegramUserID != q.TelegramChatID ? await FoxTelegramChat.Get(q.TelegramChatID) : null);

                        if (q.input_image is not null)
                        {
                            IAlbumInputMedia[] inputMedia = {
                            new InputMediaPhoto(new InputFileStream(ConvertImageToJpeg(new MemoryStream(q.input_image.Image)), "input"))
                            {
                                //Caption = $"(<a href=\"http://makefox.bot/q/{q.link_token}\">Click for Details</a>)",
                                Caption =
                                    (u is null ? "" : $"👤User: {u.display_name}\r\n") +
                                    (c is null ? "" : $"💬Chat: {c.title}\r\n") +
                                    $"🖤Prompt: " + q.settings.prompt.Left(600) + "\r\n" +
                                    $"🐊Negative: " + q.settings.negative_prompt.Left(200) + "\r\n" +
                                    $"🖥️ Size: {q.settings.width}x{q.settings.height}\r\n" +
                                    $"🪜Sampler Steps: {q.settings.steps}\r\n" +
                                    $"🧑‍🎨CFG Scale: {q.settings.cfgscale}\r\n" +
                                    $"👂Denoising Strength: {q.settings.denoising_strength}\r\n" +
                                    $"🧠Model: {q.settings.model}\r\n" +
                                    $"🌱Seed: {q.settings.seed}\r\n",
                                    //$"⚙️Worker: {q.settings.model}\r\n",
                                },
                                new InputMediaPhoto(new InputFileId(output_fileid))
                            };

                            await botClient.SendMediaGroupAsync(
                                chatId: -1002039506384,
                                media: inputMedia
                                );

                        }
                        else
                        {
                            await botClient.SendPhotoAsync(
                                chatId: -1002039506384,
                                photo: new InputFileId(output_fileid),
                                caption: (u is null ? "" : $"👤User: {u.display_name}\r\n") +
                                         (c is null ? "" : $"💬Chat: {c.title}\r\n") +
                                         $"🖤Prompt: " + q.settings.prompt.Left(600) + "\r\n" +
                                         $"🐊Negative: " + q.settings.negative_prompt.Left(200) + "\r\n" +
                                         $"🖥️ Size: {q.settings.width}x{q.settings.height}\r\n" +
                                         $"🪜Sampler Steps: {q.settings.steps}\r\n" +
                                         $"🧑‍🎨CFG Scale: {q.settings.cfgscale}\r\n" +
                                         $"🧠Model: {q.settings.model}\r\n" +
                                         $"🌱Seed: {q.settings.seed}\r\n"
                                );
                        }

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        // Pattern to match "Too Many Requests: retry after XX"
                        string pattern = @"Too Many Requests: retry after (\d+)";
                        Match match = Regex.Match(ex.Message, pattern);

                        if (match.Success)
                        {
                            // If the message matches, extract the number
                            int retryAfterSeconds = int.Parse(match.Groups[1].Value) + 3;
                            FoxLog.WriteLine($"Rate limit exceeded. Retrying after {retryAfterSeconds} seconds...");

                            // Wait for the specified number of seconds before retrying
                            await Task.Delay(retryAfterSeconds * 1000);
                        }
                        else
                        {
                            // If the message doesn't match the expected format, rethrow the exception
                            throw;
                        }
                    }

                }

            } else {
                try
                {
                    await botClient.EditMessageTextAsync(
                        chatId: q.TelegramChatID,
                        messageId: q.msg_id,
                        text: $"❌ An unexpected error occured.  Please try again."
                    );
                    FoxLog.WriteLine("SendQueue: Unexpected error; output image was null.");
                }
                catch { } //We don't care if editing fails.
            }

            //FoxLog.WriteLine("Upload Complete");
        }

        private static MemoryStream ConvertImageToJpeg(MemoryStream inputImageStream, int quality = 85)
        {
            var outputStream = new MemoryStream();
            using (var image = Image.Load(inputImageStream))
            {
                image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = quality });
                outputStream.Position = 0;

                //FoxLog.WriteLine($"ConvertImagetoJpeg({quality}%): " + Math.Round(inputImageStream.Length / 1024.0, 2) + "kb > " + Math.Round(outputStream.Length / 1024.0, 2) + "kb");
                return outputStream;
            }
        }
    }
} 