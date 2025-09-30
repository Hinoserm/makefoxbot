using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static makefoxsrv.FoxCommandHandlerOld;

namespace makefoxsrv.commands
{
    internal class FoxCmdSampler
    {

        [BotCommand(cmd: "sampler")]
        [CommandDescription("Change current AI sampler.")]
        public static async Task CmdSampler(FoxTelegram t, FoxUser user, TL.Message message)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            bool userIsPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            var cmdText = "SELECT * FROM samplers";

            MySqlCommand cmd = new MySqlCommand(cmdText, SQL);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string samplerName = reader.GetString("sampler");
                    bool isPremium = reader.GetBoolean("premium");

                    var buttonLabel = $"{samplerName}";
                    var buttonData = $"/sampler {samplerName}";

                    if (isPremium)
                    {
                        if (!userIsPremium)
                        {
                            buttonLabel = "🔒 " + buttonLabel;
                            buttonData = "/sampler premium";
                        }
                        else
                            buttonLabel = "⭐ " + buttonLabel;
                    }

                    if (samplerName == settings.Sampler)
                    {
                        buttonLabel += " ✅";
                    }

                    keyboardRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonCallback[]
                        {
                            new TL.KeyboardButtonCallback { text = buttonLabel, data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                        }
                    });
                }
            }

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "Default", data = System.Text.Encoding.UTF8.GetBytes("/sampler default") }
                }
            });

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/sampler cancel") }
                }
            });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard

            // Send the message with the inline keyboard
            await t.SendMessageAsync(
                text: "Select a sampler:",
                replyInlineMarkup: inlineKeyboard,
                replyToMessage: message
            );
        }

    }
}
