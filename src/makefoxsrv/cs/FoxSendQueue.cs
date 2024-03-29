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
                catch (WTelegram.WTException ex)
                {
                    //If we can't edit, we probably hit a rate limit with this user.

                    if (ex is RpcException rex)
                    {
                        if ((ex.Message.EndsWith("_WAIT_X") || ex.Message.EndsWith("_DELAY_X")))
                        {
                            // If the message matches, extract the number
                            int retryAfterSeconds = rex.X;
                            FoxLog.WriteLine($"Failed to send image - Rate limit exceeded. Try again after {retryAfterSeconds} seconds.");

                            await q.SetError(ex, DateTime.Now.AddSeconds(retryAfterSeconds + 65));

                            return;
                        }
                    }
                    else
                    {
                        //We don't usually care if editing fails.
                    }
                }
                catch (Exception ex) {
                    //We don't usually care if editing fails.
                }

                var new_msgid = q.msg_id;

                try
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

                    var inputImage = await FoxTelegram.Client.UploadFileAsync(ConvertImageToJpeg(new MemoryStream(q.output_image.Image)), "image.jpg");

                    //var msg = await FoxTelegram.Client.SendMediaAsync(t.Peer, "", inputImage, null, (int)q.reply_msg);

                    System.TimeSpan diffResult = DateTime.Now.Subtract(q.creation_time);
                    System.TimeSpan GPUTime = await q.GetGPUTime();
                    string messageText = $"✅ Complete! (Took {diffResult.ToPrettyFormat()} - GPU: {GPUTime.ToPrettyFormat()}";

                    var msg = await t.SendMessageAsync( 
                        media: new InputMediaUploadedPhoto() { file = inputImage },
                        text: (t.Chat is not null ? messageText : null),
                        replyInlineMarkup: (t.Chat is not null ? inlineKeyboardButtons : null),
                        replyToMessageId: (int)q.reply_msg
                        );

                    new_msgid = msg.ID;

                    if (t.Chat is null) // Only offer the buttons in private chats, not groups.
                    {
                        await t.SendMessageAsync(
                            text: messageText,
                            replyInlineMarkup: inlineKeyboardButtons
                        );
                    }
                }
                catch (WTelegram.WTException ex)
                {
                    //If we can't edit, we probably hit a rate limit with this user.

                    if (ex is RpcException rex)
                    {
                        if ((ex.Message.EndsWith("_WAIT_X") || ex.Message.EndsWith("_DELAY_X")))
                        {
                            // If the message matches, extract the number
                            int retryAfterSeconds = rex.X;
                            FoxLog.WriteLine($"Failed to send image - Rate limit exceeded. Try again after {retryAfterSeconds} seconds.");

                            await q.SetError(ex, DateTime.Now.AddSeconds(retryAfterSeconds + 65));

                            return;
                        }
                    }
                    else
                    {
                        FoxLog.WriteLine($"Failed to send image - {ex.Message}\r\n{ex.StackTrace}");
                        await q.SetError(ex);

                        return;
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Failed to send image - {ex.Message}\r\n{ex.StackTrace}");
                    await q.SetError(ex);

                    return;
                }

                try
                {
                    await t.DeleteMessage(q.msg_id);
                }
                catch { } // We don't care if editing fails

                q.msg_id = new_msgid;
                await q.SetFinished();
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