using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static makefoxsrv.FoxCommandHandlerOld;
using TL;

namespace makefoxsrv.commands
{
    internal class FoxCmdMembership
    {
        [BotCommand(cmd: "donate", hidden: true)]
        [BotCommand(cmd: "membership")]
        [CommandDescription("Purchase a membership")]
        public static async Task CmdMembership(FoxTelegram t, FoxUser user, Message message)
        {

            if (string.IsNullOrEmpty(FoxMain.settings?.TelegramPaymentToken))
                throw new Exception("Payments are currently disabled. (token not set)");

            // Define donation amounts in whole dollars
            int[] donationAmounts = new int[] { 5, 10, 20, 40, 60, 100 };

            // Initialize a list to hold TL.KeyboardButtonRow for each row of buttons
            List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

            // List to accumulate buttons for the current row
            List<TL.KeyboardButtonWebView> currentRowButtons = new List<TL.KeyboardButtonWebView>();

            var pSession = await FoxPayments.Invoice.Create(user);

            // Loop through the donation amounts and create buttons
            for (int i = 0; i < donationAmounts.Length; i++)
            {
                int amountInCents = donationAmounts[i] * 100;
                int days = FoxPayments.CalculateRewardDays(amountInCents);
                string buttonText = $"💳 ${donationAmounts[i]} ({days} days)";

                string webUrl = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?tg=1&id={pSession.UUID}&amount={amountInCents}";

                currentRowButtons.Add(new TL.KeyboardButtonWebView { text = buttonText, url = webUrl });

                // Every two buttons or at the end, add the current row to buttonRows and start a new row
                if ((i + 1) % 2 == 0 || i == donationAmounts.Length - 1)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });
                    currentRowButtons = new List<TL.KeyboardButtonWebView>(); // Reset for the next row
                }
            }

            // Add lifetime access button
            //buttonRows.Add(new TL.KeyboardButtonRow
            //{
            //    buttons = new TL.KeyboardButtonCallback[]
            //    {
            //        new() { text = "✨💰 💳 $600 (Lifetime Access!) 💰✨", data = System.Text.Encoding.UTF8.GetBytes("/donate 600 lifetime") }
            //    }
            //});

            // Add sale button
            //buttonRows.Add(new TL.KeyboardButtonRow
            //{
            //    buttons = new TL.KeyboardButtonCallback[]
            //    {
            //        new() { text = "✨💰 💳 $50 (356 Days) SALE 💰✨", data = System.Text.Encoding.UTF8.GetBytes("/donate promo50") }
            //    }
            //});

            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonUrl[]
                {
                    new() { text = "🔗 Pay in Web Browser", url = $"{FoxMain.settings.WebRootUrl}tgapp/membership.php?id={pSession.UUID}" }
                }
            });

            // Add Stars button
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "⭐ Use Telegram Stars ⭐", data = System.Text.Encoding.UTF8.GetBytes("/donate stars") }
                }
            });

            // Add cancel button on its own row at the end
            buttonRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                    new() { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/donate cancel") }
                }
            });



            var inlineKeyboard = t.Chat is not null ? null : new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() };

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("<b>MakeFox Membership – Unlock Exclusive Benefits</b>\n"); //Intentional extra newline.

            if (user.datePremiumExpires > DateTime.Now)
            {
                //User is already a premium member
                sb.AppendLine("Thank you for purchasing a MakeFox membership!\n");
                sb.AppendFormat("Your membership is active until <b>{0:MMMM d\\t\\h yyyy}</b>.\n", user.datePremiumExpires);
                sb.AppendLine("\nYou can purchase additional days, which will be added to your existing membership time.");
            }
            else
            {
                sb.AppendLine("Thank you for considering a membership. <i>MakeFox Group, Inc.</i> is a registered US non-profit, and your support is crucial for the development and maintenance of our platform.");
                if (user.datePremiumExpires < DateTime.Now)
                {
                    sb.AppendFormat("\nYour previous membership expired on <b>{0:MMMM d\\t\\h yyyy}</b>.\n", user.datePremiumExpires);
                }
            }

            sb.AppendLine("\n<b>Membership Benefits:</b>\n");
            sb.AppendLine(" - <b>High-Resolution Image Enhancements:</b> Members enjoy nearly unlimited enhancements and variations, subject to fair usage limits.\n");

            sb.AppendLine(" - <b>Flexible Image Dimensions:</b> Create images in any shape and size up to 3.7 million pixels.\n");

            sb.AppendLine(" - <b>Queue Priority:</b> Your requests get placed first in the queue, allowing for shorter wait times.\n");

            sb.AppendLine(" - <b>Early Access:</b> Be the first to try new experimental models and features.\n");

            sb.AppendLine("<a href=\"https://telegra.ph/MakeFox-Membership-06-07\"><b>Click here for more information.</b></a>\n");

            sb.Append("<i>Note: Membership purchases are not tax-deductible.</i>");

            if (t.Chat is not null)
            {
                sb.AppendLine($"\n\n<b>You cannot purchase a membership from within a group chat.\n\nTo purchase a membership, please contact @{FoxTelegram.BotUser?.MainUsername} directly.</b>");
            }

            var msg = sb.ToString();

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            var sentMessage = await t.SendMessageAsync(
                text: msg,
                replyInlineMarkup: inlineKeyboard,
                entities: entities,
                disableWebPagePreview: true,
                replyToMessage: message
            );

            pSession.TelegramMessageId = sentMessage.ID;
            pSession.TelegramPeerId = sentMessage.peer_id;
            await pSession.Save();
        }

    }
}
