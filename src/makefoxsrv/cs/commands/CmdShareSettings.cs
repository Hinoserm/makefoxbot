using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv.commands
{
    internal class FoxCmdShareSettings
    {
        [BotCallable]
        public static async Task cbToggleSharing(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbShowAnonymityMenu(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbSelectAnonymity(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbToggleShowPrompts(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbToggleAllowDownload(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbDone(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbShowSharingMenu(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await ShowSharingMenu(t, user, new TL.Message() { id = query.msg_id }, editMessage: true);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        public static async Task ShowSharingMenu(FoxTelegram t, FoxUser user, TL.Message message, bool editMessage = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🦊 *Public Post Sharing Settings*");
            sb.AppendLine();
            sb.AppendLine("When Public Sharing is enabled, clicking the 👍 button on a generated image will submit it to our moderation team for review. If approved, it may appear on the public gallery at @MakeFoxArt.");
            sb.AppendLine();
            sb.AppendLine("You can control how your identity appears when posting:");
            sb.AppendLine("• Remain anonymous");
            sb.AppendLine("• Display your @username");
            sb.AppendLine("• Display a linked group or channel name");
            sb.AppendLine();
            sb.AppendLine("You can also decide whether to:");
            sb.AppendLine("• Show the original prompt and generation settings");
            sb.AppendLine("• Allow or disallow downloads of the original, uncompressed image");
            sb.AppendLine();
            sb.AppendLine("Adjust the options below to match how public you want your shared creations to be.");

            var buttonRows = await BuildShareButtons(t, user, message);

            if (editMessage)
            {
                await t.EditMessageAsync(
                    text: sb.ToString(),
                    id: message.ID,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: sb.ToString(),
                    replyToMessage: message,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }
        }

        public static async Task<List<TL.KeyboardButtonRow>> BuildShareButtons(FoxTelegram t, FoxUser user, TL.Message message)
        {
            List<TL.KeyboardButtonRow> buttonRows = new();

            // Replace these with your real user settings
            var enabled = false;
            var showPrompts = false;
            var allowDownload = false;

            // Public Posting toggle
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback
                    {
                        text = (enabled ? "❌ Disable Public Sharing" : "✅ Enable Public Sharing"),
                        data = FoxCallbackHandler.BuildCallbackData(cbToggleSharing, user.UID)
                    }
                }
            });

            // Go to anonymity submenu
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback
                    {
                        text = "🕶 Select Anonymity Mode",
                        data = FoxCallbackHandler.BuildCallbackData(cbShowAnonymityMenu, user.UID)
                    }
                }
            });

            // Show/Hide prompts toggle
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback
                    {
                        text = (showPrompts ? "❌ Hide Prompts" : "✅ Show Prompts"),
                        data = FoxCallbackHandler.BuildCallbackData(cbToggleShowPrompts, user.UID)
                    }
                }
            });

            // Allow/Disallow download toggle
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback
                    {
                        text = (allowDownload ? "❌ Disallow Downloads" : "✅ Allow Downloads"),
                        data = FoxCallbackHandler.BuildCallbackData(cbToggleAllowDownload, user.UID)
                    }
                }
            });

            // Done button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback
                    {
                        text = "Done",
                        data = FoxCallbackHandler.BuildCallbackData(cbDone, user.UID)
                    }
                }
            });

            return buttonRows;
        }


    }
}
