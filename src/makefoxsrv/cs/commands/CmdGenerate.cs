using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.commands
{
    internal class FoxCmdGenerate
    {
        [BotCommand(cmd: "img2img")]
        [CommandDescription("Run an img2img generation.  Requires you to have previously uploaded an image.")]
        public static async Task CmdImg2Img(FoxTelegram t, FoxUser user, TL.Message message, String? prompt)
        {
            await FoxGenerate.HandleCmdGenerate(t, message, user, prompt, FoxQueue.QueueType.IMG2IMG);
        }

        [BotCommand(cmd: "generate")]
        [CommandDescription("Run a standard txt2img generation.")]
        public static async Task CmdGenerate(FoxTelegram t, FoxUser user, TL.Message message, String? prompt)
        {
            await FoxGenerate.HandleCmdGenerate(t, message, user, prompt, FoxQueue.QueueType.TXT2IMG);
        }

        [BotCommand(cmd: "current")]
        [CommandDescription("Show all of your currently configured settings for this chat or group.")]
        public static async Task CmdCurrent(FoxTelegram t, FoxUser user, TL.Message message)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);
            await settings.Save(); // Save the settings just in case this is a new chat, to init the defaults so we don't look like a liar later.

            await t.SendMessageAsync(
                text: $"🖤Prompt: {settings.Prompt}\r\n" +
                      $"🐊Negative: {settings.NegativePrompt}\r\n" +
                      $"🖥️Size: {settings.Width}x{settings.Height}\r\n" +
                      $"🪜Sampler: {settings.Sampler} ({settings.steps} steps)\r\n" +
                      $"🧑‍🎨CFG Scale: {settings.CFGScale}\r\n" +
                      $"👂Denoising Strength: {settings.DenoisingStrength}\r\n" +
                      $"🧠Model: {settings.ModelName}\r\n" +
                      $"🌱Seed: {settings.Seed}\r\n",
                replyToMessage: message
            );
        }

        [BotCommand(cmd: "select")]
        [CommandDescription("Send this in a reply to any message containing an image to select it for /img2img")]
        public static async Task CmdSelect(FoxTelegram t, FoxUser user, TL.Message message)
        {
            var img = await FoxImage.SaveImageFromReply(t, message);

            if (img is null)
            {
                await t.SendMessageAsync(
                        text: "❌ Error: That message doesn't contain an image.  You must send this command as a reply to a message containing an image.",
                        replyToMessage: message
                        );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.SelectedImage = img.ID;

            await settings.Save();

            try
            {
                TL.Message waitMsg = await t.SendMessageAsync(
                    text: "✅ Image saved and selected as input for /img2img",
                    replyToMessage: message
                );
            }
            catch
            {
                TL.Message waitMsg = await t.SendMessageAsync(
                    text: "✅ Image saved and selected as input for /img2img"
                );
            }

        }

    }
}
