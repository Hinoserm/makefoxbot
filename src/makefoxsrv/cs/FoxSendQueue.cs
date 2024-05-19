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
            FoxLog.WriteLine($"Sending task {q.ID} to Telegram...", LogLevel.DEBUG);

            try
            {
                var OutputImage = await q.GetOutputImage();

                if (OutputImage is not null)
                {
                    var t = q.Telegram;

                    if (t is null)
                    {
                        var ex = new Exception("Telegram object not initalized!");
                        await q.SetError(ex);

                        FoxLog.WriteLine($"Failed to send image - {ex.Message}\r\n{ex.StackTrace}");

                        throw ex;
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
                                // If the message matches, extract the number
                                int retryAfterSeconds = rex.X;
                                FoxLog.WriteLine($"Failed to send image {q.ID}:{OutputImage.ID} to {q.User?.UID} - Rate limit exceeded. Try again after {retryAfterSeconds} seconds.");

                                await q.SetError(ex, DateTime.Now.AddSeconds(retryAfterSeconds + 65));

                                return;
                            }
                        }
                        else
                        {
                            //We don't usually care if editing fails.
                        }
                    }
                    catch (Exception ex)
                    {
                        //We don't usually care if editing fails.
                    }

                    var oldMessageID = q.MessageID;

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
                                        new TL.KeyboardButtonCallback { text = "👍", data = System.Text.Encoding.ASCII.GetBytes("/vote up " + q.ID) },
                                        new TL.KeyboardButtonCallback { text = "👎", data = System.Text.Encoding.ASCII.GetBytes("/vote down " + q.ID) },
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

                        var inputImage = await FoxTelegram.Client.UploadFileAsync(ConvertImageToJpeg(new MemoryStream(OutputImage.Image), 80, 1280), $"{FoxTelegram.Client.User.username}_smimage_{q.ID}.jpg");

                        //var msg = await FoxTelegram.Client.SendMediaAsync(t.Peer, "", inputImage, null, (int)q.reply_msg);

                        System.TimeSpan diffResult = DateTime.Now.Subtract(q.DateCreated);
                        System.TimeSpan GPUTime = await q.GetGPUTime();
                        string messageText = $"✅ Complete! (Took {diffResult.ToPrettyFormat()} - GPU: {GPUTime.ToPrettyFormat()}";

                        if (q.Settings.width > 1280 || q.Settings.height > 1280)
                        {
                            messageText += $"\n\n⚠️ Image dimensions exceed Telegram's maximum image preview size.  For best quality, click below to download the full resoluton file.";
                        }

                        var msg = await t.SendMessageAsync(
                            media: new InputMediaUploadedPhoto() { file = inputImage },
                            text: (t.Chat is not null ? messageText : null),
                            replyInlineMarkup: (t.Chat is not null ? inlineKeyboardButtons : null),
                            replyToMessageId: q.ReplyMessageID ?? 0
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
                            else if (ex.Message == "USER_IS_BLOCKED")
                            {
                                //User is blocked, so we can't send them messages.
                                FoxLog.WriteLine($"Failed to send image - User is blocked.  Cancelling.");
                                await q.SetCancelled(true);

                                return;
                            }
                            else
                            {
                                FoxLog.WriteLine($"Failed to send image - {ex.Message}\r\n{ex.StackTrace}");
                                await q.SetError(ex);

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
                        await t.DeleteMessage(oldMessageID);
                    }
                    catch { } // We don't care if editing fails
                }
                else
                {
                    try
                    {
                        if (q.ReplyMessageID is not null && q.Telegram is not null)
                        {
                            await q.Telegram.EditMessageAsync(
                                id: q.ReplyMessageID.Value,
                                text: $"❌ An unexpected error occured.  Please try again."
                            );
                        }

                        FoxLog.WriteLine("SendQueue: Unexpected error; output image was null.");
                    }
                    catch { } //We don't care if editing fails.
                }

                //FoxLog.WriteLine("Upload Complete");
                FoxLog.WriteLine($"Done sending task {q.ID} to Telegram.", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Failed to send image - {ex.Message}\r\n{ex.StackTrace}");
                await q.SetError(ex);
            }
        }

        private static MemoryStream ConvertImageToJpeg(MemoryStream inputImageStream, int quality = 85, uint? maxDimension = 1280)
        {
            var outputStream = new MemoryStream();
            using (var image = Image.Load(inputImageStream))
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

                image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = quality });
                outputStream.Position = 0;

                return outputStream;
            }
        }
    }
} 