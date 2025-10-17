using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv.commands
{
    internal class FoxCmdSettings
    {
        [BotCallable]
        public static async Task cbShowSettings(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await ShowSettings(t, user, new TL.Message() { id = query.msg_id }, editMessage: true);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbDone(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            try
            {
                await t.EditMessageAsync(
                    text: "✅ Done.",
                    id: query.msg_id,
                    replyInlineMarkup: null
                );
            }
            catch (Exception ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // Do nothing
            }

            await t.SendCallbackAnswer(query.query_id, 0);
        }


        public static async Task ShowSettings(FoxTelegram t, FoxUser user, Message message, bool editMessage = false)
        {
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                        new TL.KeyboardButtonCallback {
                            text = "\U0001f9e0 Chatbot Settings (LLM)",
                            data = FoxCallbackHandler.BuildCallbackData(FoxCmdLLMSettings.cbShowSettings, user.UID, true)
                        },
                }
            });

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                        new TL.KeyboardButtonCallback {
                            text = "📢 Public Posting Settings",
                            data = FoxCallbackHandler.BuildCallbackData(FoxCmdShareSettings.cbShowSharingMenu, user.UID) },
                }
            });


            // Add done button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback { text = "Done", data = FoxCallbackHandler.BuildCallbackData(cbDone, user.UID ) },
                }
            });

            var sb = new StringBuilder();
            sb.AppendLine("Settings.");
            sb.AppendLine();

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

        [BotCommand(cmd: "settings")]
        public static async Task CmdSettings(FoxTelegram t, FoxUser user, Message message, string? args = null)
        {
            await ShowSettings(t, user, message);
        }

    }
}
