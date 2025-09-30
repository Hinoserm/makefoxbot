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
        [BotCommand(cmd: "prompt")]
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

        [BotCommand(cmd: "setsteps")]
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
        [BotCommand(cmd: "denoise")]
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
    }
}