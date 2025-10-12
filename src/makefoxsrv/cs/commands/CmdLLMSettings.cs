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
    internal class FoxCmdLLMSettings
    {
        [BotCallable]
        public static async Task cbSelectPersona(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId, string? persona = null)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            var llmSettings = await FoxLLMUserSettings.GetSettingsAsync(user);

            if (!string.IsNullOrEmpty(persona))
            {
                llmSettings.SelectedPersona = persona;
                await llmSettings.Save();
            }

            // Load personalities from database

            await using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            var sql = "SELECT * FROM llm_personalities ORDER BY name ASC";
            await using var cmd = new MySqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            string? selectedPersonaDescription = null;

            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();
            List<TL.KeyboardButtonBase> currentRow = new List<TL.KeyboardButtonBase>();

            while (await reader.ReadAsync())
            {
                var personaName = reader.GetString("name");
                var displayName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(personaName.ToLowerInvariant());
                var buttonData = FoxCallbackHandler.BuildCallbackData(cbSelectPersona, user.UID, personaName);

                bool isSelected = string.Equals(
                    llmSettings.SelectedPersona,
                    personaName,
                    StringComparison.OrdinalIgnoreCase
                );

                if (isSelected)
                    selectedPersonaDescription = reader.GetString("description");

                currentRow.Add(new TL.KeyboardButtonCallback
                {
                    text = displayName + (isSelected ? " ✅" : ""),
                    data = System.Text.Encoding.UTF8.GetBytes(buttonData)
                });

                // Once we have two buttons, push the row and start a new one
                if (currentRow.Count == 3)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRow.ToArray() });
                    currentRow.Clear();
                }
            }

            // Add any leftover single button
            if (currentRow.Count > 0)
                buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRow.ToArray() });

            // Add back and done buttons
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback { text = "< Back", data = System.Text.Encoding.UTF8.GetBytes(FoxCallbackHandler.BuildCallbackData(cbShowSettings, user.UID)) },
                    new TL.KeyboardButtonCallback { text = "Done", data = System.Text.Encoding.UTF8.GetBytes(FoxCallbackHandler.BuildCallbackData(cbDone, user.UID)) },
                }
            });

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("🧠 Choose a personality for the AI assistant from the options below.");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(selectedPersonaDescription))
            {
                sb.AppendLine($"Currently selected:");
                sb.AppendLine($"*{llmSettings.SelectedPersona}* -  {selectedPersonaDescription}");
            }
            else
            {
                sb.AppendLine($"\nCurrent Personality: *{llmSettings.SelectedPersona}*");
            }

            sb.AppendLine();
            sb.AppendLine("⚠️ It is strongly recommended to Clear History after changing the personality.");

            try
            {
                await t.EditMessageAsync(
                    text: sb.ToString(),
                    id: query.msg_id,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }
            catch (Exception ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // Do nothing
            }

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbClearHistory(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            var llmSettings = await FoxLLMUserSettings.GetSettingsAsync(user);

            await llmSettings.ClearHistoryAsync();

            // Show back button
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback { text = "< Back", data = System.Text.Encoding.UTF8.GetBytes(FoxCallbackHandler.BuildCallbackData(cbShowSettings, user.UID)) },
                }
            });

            try
            {
                await t.EditMessageAsync(
                    text: "✅ History cleared.",
                    id: query.msg_id,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }
            catch (Exception ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // Do nothing
            }
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
            catch (Exception ex) when(ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // Do nothing
            }

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable]
        public static async Task cbShowSettings(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            if (user.UID != userId)
                throw new Exception("This is someone else's button.");

            await ShowLLMSettings(t, user, new TL.Message() { id = query.msg_id }, EditMessage: true);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        public static async Task ShowLLMSettings(FoxTelegram t, FoxUser user, Message message, bool EditMessage = false)
        {
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            var personaButtonData = FoxCallbackHandler.BuildCallbackData(cbSelectPersona, user.UID, null);

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                        new TL.KeyboardButtonCallback { text = "Set Personality", data = System.Text.Encoding.UTF8.GetBytes(personaButtonData) },
                }
            });

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                        new TL.KeyboardButtonCallback { text = "Clear History", data = Encoding.UTF8.GetBytes(FoxCallbackHandler.BuildCallbackData(cbClearHistory, user.UID)) },
                }
            });

            // Add done button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonBase[]
                {
                    new TL.KeyboardButtonCallback { text = "Done", data = System.Text.Encoding.UTF8.GetBytes(FoxCallbackHandler.BuildCallbackData(cbDone, user.UID)) },
                }
            });

            var msgStr = "🧠 LLM Settings";

            if (EditMessage)
            {
                await t.EditMessageAsync(
                    text: msgStr,
                    id: message.ID,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }
            else
            {
                await t.SendMessageAsync(
                    text: msgStr,
                    replyToMessage: message,
                    replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                );
            }

        }


        [BotCommand(cmd: "llm")]
        public static async Task CmdLLMSettings(FoxTelegram t, FoxUser user, Message message)
        {
            await ShowLLMSettings(t, user, message);
        }

    }
}
