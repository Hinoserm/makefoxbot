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

namespace makefoxbot
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
                try
                {
                    await botClient.EditMessageTextAsync(
                        chatId: q.TelegramChatID,
                        messageId: q.msg_id,
                        text: $"⏳ Uploading..."
                    );
                }
                catch { } //We don't care if editing fails.

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
                        var msg = await botClient.SendPhotoAsync(
                            chatId: q.TelegramChatID,
                            photo: InputFile.FromStream(ConvertImageToJpeg(new MemoryStream(q.output_image.Image)))
                            );
                        output_fileid = msg.Photo.First().FileId;
                    }
                    else
                        throw;
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

                    await botClient.SendTextMessageAsync(
                        chatId: q.TelegramChatID,
                        text: "✅ Complete! (Took " + diffResult.ToPrettyFormat() + ")",
                        replyMarkup: inlineKeyboard
                    );
                }

                // new InputFileId(output_fileid)


                try
                {
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
                                $"🌱Seed: {q.settings.seed}\r\n",
                            //ParseMode = ParseMode.Html
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
                                     $"🌱Seed: {q.settings.seed}\r\n"
                            );
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR SENDING TO GROUP !!!!! " + ex.Message);
                }
            } else {
                try
                {
                    await botClient.EditMessageTextAsync(
                        chatId: q.TelegramChatID,
                        messageId: q.msg_id,
                        text: $"❌ An unexpected error occured.  Please try again."
                    );
                }
                catch { } //We don't care if editing fails.
            }

            await q.Finish();

            //Console.WriteLine("Upload Complete");
        }

        private static MemoryStream ConvertImageToJpeg(MemoryStream inputImageStream, int quality = 85)
        {
            var outputStream = new MemoryStream();
            using (var image = Image.Load(inputImageStream))
            {
                image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = quality });
                outputStream.Position = 0;

                Console.WriteLine($"ConvertImagetoJpeg({quality}%): " + Math.Round(inputImageStream.Length / 1024.0, 2) + "kb > " + Math.Round(outputStream.Length / 1024.0, 2) + "kb");
                return outputStream;
            }
        }
    }
} 