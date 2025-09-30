using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static makefoxsrv.FoxCommandHandlerOld;
using TL;

namespace makefoxsrv.commands
{
    internal class FoxCmdTags
    {
        [BotCommand(cmd: "tags")]
        [CommandDescription("Send this in a reply to an image to get a list of AI-predicted E621 tags.")]
        internal static async Task CmdTag(FoxTelegram t, FoxUser user, Message message)
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

            using Image<Rgba32> image = Image.Load<Rgba32>(stickerImg.Image);

            // Here we enable drop shadow with custom parameters. 

            var startTime = DateTime.Now;
            FoxONNXImageTagger tagger = new FoxONNXImageTagger();
            var predictions = tagger.ProcessImage(image, 0.2f);
            var elapsedTime = DateTime.Now - startTime;


            string msgText = predictions != null && predictions.Count > 0
                ? "*Predicted Tags:*\r\n\r\n" + string.Join(", ", predictions.Select(p => $"`{p.Key}`"))
                : "*No tags found.*";

            msgText += $"\r\n\n*Processing time: {Math.Round(elapsedTime.TotalMilliseconds, 0)}ms*";

            var msgEntities = FoxTelegram.Client.MarkdownToEntities(ref msgText);

            await t.SendMessageAsync(
                               replyToMessage: message,
                               text: msgText,
                               entities: msgEntities
            );
        }

    }
}
