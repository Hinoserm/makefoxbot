using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static makefoxsrv.FoxCommandHandlerOld;

namespace makefoxsrv.commands
{
    internal class FoxCmdSet
    {
        [BotCommand(cmd: "setnegative")]
        [BotCommand(cmd: "negative")]
        [CommandDescription("Set your negative prompt for this chat or group.  Leave blank to clear.")]
        public static async Task CmdSetNegative(FoxTelegram t, FoxUser user, TL.Message message, String? negativePrompt)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (string.IsNullOrEmpty(negativePrompt))
            {
                await t.SendMessageAsync(
                    text: $"Negative prompt: {settings.NegativePrompt ?? "[empty]"}",
                    replyToMessage: message
                );
                return;
            }

            settings.NegativePrompt = negativePrompt; //.Replace("\n", ", ");

            await settings.Save();

            await t.SendMessageAsync(
                text: (settings.NegativePrompt.Length > 0 ? $"✅ Negative prompt set." : "✅ Negative prompt cleared."),
                replyToMessage: message
            );
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setprompt")]
        [BotCommand(cmd: "prompt", hidden: true)]
        [CommandDescription("Set or view your prompt for this chat or group.")]
        public static async Task CmdSetPrompt(FoxTelegram t, FoxUser user, TL.Message message, String? prompt)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (!string.IsNullOrEmpty(prompt))
            {

                settings.Prompt = prompt; //.Replace("\n", ", ");

                await settings.Save();

                await t.SendMessageAsync(
                    text: $"✅ Prompt set.",
                    replyToMessage: message
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: $"🖤Current prompt: " + settings.Prompt,
                    replyToMessage: message
                );
            }
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setsteps", hidden: true)]
        [BotCommand(cmd: "steps")]
        [CommandDescription("Set or view your sampler steps for this chat or group.  Range varies based on load and account type.")]
        public static async Task CmdSetSteps(FoxTelegram t, FoxUser user, TL.Message message, int? numSteps)
        {
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (numSteps is null)
            {
                await t.SendMessageAsync(
                    text: "Current steps value: " + settings.steps,
                    replyToMessage: message
                );
                return;
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (numSteps > 20 && !isPremium)
            {
                await t.SendMessageAsync(
                    text: "❌ Only members can exceed 20 steps.\r\n\r\nPlease consider a /membership",
                    replyToMessage: message
                );

                if (settings.steps > 20)
                {
                    settings.steps = 20;

                    await settings.Save();
                }
                return;
            }
            else if (numSteps < 1 || (numSteps > 30 && !user.CheckAccessLevel(AccessLevel.ADMIN)))
            {
                await t.SendMessageAsync(
                    text: "❌ Value must be above 1 and below 30.",
                    replyToMessage: message
                );

                return;
            }

            settings.steps = (int)numSteps;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Steps set to {numSteps}.",
                replyToMessage: message
            );
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setdenoise")]
        [BotCommand(cmd: "denoise", hidden: true)]
        [CommandDescription("Set or view your Denoise Strength for this chat or group, used only by img2img. Range 0 - 1.0.")]
        public static async Task CmdSetDenoise(FoxTelegram t, FoxUser user, TL.Message message, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var stepstr = message.message.Split(' ');

            if (stepstr.Count() < 2)
            {
                await t.SendMessageAsync(
                    text: "Current Denoising Strength: " + settings.DenoisingStrength,
                    replyToMessage: message
                );
                return;
            }

            if (stepstr.Count() > 2 || !decimal.TryParse(stepstr[1], out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 1)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 1.0.",
                    replyToMessage: message
                );

                return;
            }

            settings.DenoisingStrength = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Denoising Strength set to {cfgscale}.",
                replyToMessage: message
            );
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setseed")]
        [BotCommand(cmd: "seed")]
        [CommandDescription("Set the seed value for the next generation. Default: -1 (random)")]
        public static async Task CmdSetSeed(FoxTelegram t, FoxUser user, TL.Message message, String? argument)
        {
            int seed = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "Current Seed: " + settings.Seed,
                    replyToMessage: message
                );
                return;
            }

            if (!int.TryParse(argument, out seed))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            settings.Seed = seed;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ Seed set to {seed}.",
                replyToMessage: message
            );
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setscale", hidden: true)]
        [BotCommand(cmd: "setcfg")]
        [BotCommand(cmd: "cfg")]
        [CommandDescription("Set or view your CFG Scale for this chat or group. Range 0 - 99.0.")]
        public static async Task CmdSetCFG(FoxTelegram t, FoxUser user, TL.Message message, String? argument)
        {

            decimal cfgscale = 0;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "Current CFG Scale: " + settings.CFGScale,
                    replyToMessage: message
                );
                return;
            }

            if (!decimal.TryParse(argument, out cfgscale))
            {
                await t.SendMessageAsync(
                    text: "❌You must provide a numeric value.",
                    replyToMessage: message
                );

                return;
            }

            cfgscale = Math.Round(cfgscale, 2);

            if (cfgscale < 0 || cfgscale > 99)
            {
                await t.SendMessageAsync(
                    text: "❌Value must be between 0 and 99.0.",
                    replyToMessage: message
                );

                return;
            }

            settings.CFGScale = cfgscale;

            await settings.Save();

            await t.SendMessageAsync(
                text: $"✅ CFG Scale set to {cfgscale}.",
                replyToMessage: message
            );
        }

        //------------------------------------------------------

        [BotCommand(cmd: "setsize")]
        [BotCommand(cmd: "size")]
        [CommandDescription("Change the size of the output, e.g. /setsize 768x768")]
        public static async Task CmdSetSize(FoxTelegram t, FoxUser user, TL.Message message, String? argument)
        {
            int width;
            int height;

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            if (argument is null || argument.Length <= 0)
            {
                await t.SendMessageAsync(
                    text: "🖥️ Current size: " + settings.Width + "x" + settings.Height,
                    replyToMessage: message
                );
                return;
            }

            var args = argument.ToLower().Split("x");

            if (args.Length != 2 || args[0] is null || args[1] is null ||
                !int.TryParse(args[0].Trim(), out width) || !int.TryParse(args[1].Trim(), out height))
            {
                await t.SendMessageAsync(
                    text: "❌ Value must be in the format of [width]x[height].  Example: /setsize 768x768",
                    replyToMessage: message
                );
                return;
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if ((width < 512 || height < 512) && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Dimension should be at least 512 pixels.",
                    replyToMessage: message
                );
                return;
            }

            if ((width > 1024 || height > 1024) && !isPremium)
            {
                await t.SendMessageAsync(
                    text: "❌ Only premium users can exceed 1024 pixels in any dimension.\n\nPlease consider becoming a premium member: /donate",
                    replyToMessage: message
                );
                return;
            }
            else if ((width * height) > (2048 * 2048) && !user.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ Total image pixel count cannot be greater than 2048x2048",
                    replyToMessage: message
                );
                return;
            }

            var msgString = "";

            /*
            (int normalizedWidth, int normalizedHeight) = FoxImage.NormalizeImageSize(width, height);

            if (normalizedWidth != width || normalizedHeight != height)
            {
                msgString += $"⚠️ For optimal performance, your setting has been adjusted to: {normalizedWidth}x{normalizedHeight}.\r\n\r\n";
                msgString += $"⚠️To override, type /setsize {width}x{height} force.  You may receive less favorable queue priority.\r\n\r\n";

                width = normalizedWidth;
                height = normalizedHeight;

            } */

            msgString += $"✅ Size set to: {width}x{height}";

            settings.Width = (uint)width;
            settings.Height = (uint)height;

            await settings.Save();

            await t.SendMessageAsync(
                text: msgString,
                replyToMessage: message
            ); ;
        }

    }
}