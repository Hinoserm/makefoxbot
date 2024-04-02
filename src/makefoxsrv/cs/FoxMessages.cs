using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using static System.Net.Mime.MediaTypeNames;
using WTelegram;
using makefoxsrv;


//This class deals with building and sending Telegram messages to the user.

namespace makefoxsrv
{
    internal class FoxMessages
    {
        public static async Task SendWelcome(FoxTelegram t, FoxUser user, int replyMessageID = 0, int editMessage = 0)
        {
            try
            {
                var inlineKeyboardButtons = user.Strings.GenerateLanguageButtons();

                if (user.TermsAgreedDate is null)
                {
                    inlineKeyboardButtons.rows = inlineKeyboardButtons.rows.Concat(new TL.KeyboardButtonRow[]
                    {
                        new TL.KeyboardButtonRow
                        {
                            buttons = new TL.KeyboardButtonCallback[]
                            {
                                new TL.KeyboardButtonCallback { text = user.Strings.Get("Terms.AgreeButton"), data = System.Text.Encoding.ASCII.GetBytes("/terms agree") },
                            }
                        }
                    }).ToArray();
                }

                var message = user.Strings.Get("Welcome");

                if (user.TermsAgreedDate is null)
                {
                    message += "\n\n" + user.Strings.Get("Terms.AgreePrompt");
                }
                else
                {
                    message += "\n\n" + user.Strings.Get("Terms.UserAgreed");
                }

                var entities = FoxTelegram.Client.HtmlToEntities(ref message);


                if (editMessage != 0)
                {
                    await t.EditMessageAsync(
                        id: editMessage,
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
                else
                {
                    await t.SendMessageAsync(
                        text: message,
                        entities: entities,
                        replyInlineMarkup: inlineKeyboardButtons,
                        replyToMessageId: replyMessageID

                    );

                }
            } catch (WTelegram.WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
            {
                // We don't care if the message is not modified
            }
        }
    }
}
