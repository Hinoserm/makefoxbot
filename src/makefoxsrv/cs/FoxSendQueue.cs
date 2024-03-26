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

        public static async Task Send(FoxQueue q)
        {
            if (q.output_image is not null)
            {
                var t = q.telegram;

                if (t is null)
                    throw new Exception("Telegram object not initalized!");

                await q.SetSending();

                try
                {
                    await t.EditMessageAsync(
                        id: q.msg_id,
                        text: $"⏳ Uploading..."
                    );
                }
                catch (Exception ex) {

                    FoxLog.WriteLine("Error: " + ex.Message);

                } //We don't care if editing fails.

                string? output_fileid = null;

                try
                {
                    var inputImage = await t.botClient.UploadFileAsync(new MemoryStream(q.output_image.Image), "image.png");

                    var msg = await t.botClient.SendMediaAsync(t.Peer, "", inputImage, null, (int)q.reply_msg);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Bad Request: message to reply not found"))
                    {
                        try
                        {
                            var inputImage = await t.botClient.UploadFileAsync(new MemoryStream(q.output_image.Image), "image.png");

                            var msg = await t.botClient.SendMediaAsync(q.telegram.Peer, "", inputImage);
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
                    await t.DeleteMessage(q.msg_id);
                }
                catch { } // We don't care if editing fails

                //if (true || q.TelegramChatID == q.TelegramUserID) // Only offer the buttons in private chats, not groups.
                {
                    // Construct the inline keyboard buttons and rows
                    var inlineKeyboardButtons = new ReplyInlineMarkup()
                    {
                        rows = new TL.KeyboardButtonRow[]
                        {
                            new TL.KeyboardButtonRow {
                                buttons = new TL.KeyboardButtonCallback[]
                                {
                                    new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.id) },
                                    new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.id) },
                                    new TL.KeyboardButtonCallback { text = "💾", data = System.Text.Encoding.ASCII.GetBytes("/download " + q.id)},
                                    new TL.KeyboardButtonCallback { text = "🎨", data = System.Text.Encoding.ASCII.GetBytes("/select " + q.id)},
                                }
                            },
                            new TL.KeyboardButtonRow
                            {
                                buttons = new TL.KeyboardButtonCallback[]
                                {
                                    new TL.KeyboardButtonCallback { text = "Show Details", data = System.Text.Encoding.ASCII.GetBytes("/info " + q.id)},
                                }
                            }
                        }
                    };

                    System.TimeSpan diffResult = DateTime.Now.Subtract(q.creation_time);
                    System.TimeSpan GPUTime = await q.GetGPUTime();

                    await t.SendMessageAsync(
                        text: $"✅ Complete! (Took {diffResult.ToPrettyFormat()} - GPU: {GPUTime.ToPrettyFormat()}",
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }

                // new InputFileId(output_fileid)

                await q.Finish();

                bool success = false;
                //while (!success)
            } else {
                try
                {
                    await q.telegram.EditMessageAsync(
                        id: q.msg_id,
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