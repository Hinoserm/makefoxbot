using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv.commands
{
    internal class FoxCmdAdminPayments
    {

        [BotCallable(adminOnly: true)]
        public static async Task cbShowPayments(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            var targetUser = await FoxUser.GetByUID(userId);

            if (targetUser is null)
                throw new Exception($"Cannot find user #{userId}");

            await HandleShowPayments(t, user, new TL.Message() { id = query.msg_id }, targetUser);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCommand(cmd: "admin", sub: "payments", adminOnly: true)]
        public static async Task HandleShowPayments(FoxTelegram t, FoxUser user, TL.Message message, FoxUser targetUser)
        {
            var payments = new List<(long id, DateTime date, decimal amount, int days, string currency, string provider)>();
            decimal total = 0;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                        SELECT *
                        FROM user_payments
                        WHERE uid = @userId
                        ORDER BY id ASC";
                    cmd.Parameters.AddWithValue("@userId", targetUser.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long id = reader.GetInt64("id");
                            DateTime date = reader.GetDateTime("date");
                            int days = reader.GetInt32("days");
                            string currency = reader.GetString("currency");
                            string provider = reader.GetString("type");

                            int inAmount = reader.GetInt32("amount");
                            double amount = currency == "XTR" ? inAmount * 0.013 : inAmount / 100; // Convert cents to decimal format
                            total += (decimal)amount;

                            payments.Add((id, date, (decimal)amount, days, currency, provider));
                        }
                    }
                }
            }

            if (payments.Count == 0)
            {
                await t.SendMessageAsync(
                    text: $"ℹ️ User {targetUser.UID} has no recorded payments.",
                    replyToMessage: message
                );
            }
            else
            {
                var paymentDetails = payments.Select(p => $"{p.id}: ${p.amount:F2} {p.currency}, {p.days} days, {p.provider}, {p.date}");
                var paymentList = string.Join("\n", paymentDetails);

                await t.SendMessageAsync(
                    text: $"📋 Payment history for user {targetUser.UID}:\n{paymentList}\n\nTotal: {payments.Count()} transactions (${total:F2})",
                    replyToMessage: message
                );
            }
        }

        [BotCommand(cmd: "admin", sub: "premium", adminOnly: true)]
        public static async Task HandlePremium(FoxTelegram t, FoxUser user, Message message, FoxUser targetUser, string strTimeSpan)
        {
            TimeSpan timeSpan = FoxStrings.ParseTimeSpan(strTimeSpan);
            string action = timeSpan.TotalSeconds >= 0 ? "added to" : "subtracted from";

            DateTime oldExpiry = targetUser.datePremiumExpires ?? DateTime.Now;
            DateTime newExpiry = oldExpiry.Add(timeSpan);

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "UPDATE users SET date_premium_expires = @date_premium_expires WHERE id = @uid";
                    cmd.Parameters.AddWithValue("@date_premium_expires", newExpiry);
                    cmd.Parameters.AddWithValue("@uid", targetUser.UID);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await targetUser.SetPremiumDate(newExpiry);

            string responseMessage = $"✅ {Math.Abs(timeSpan.TotalSeconds)} seconds have been {action} user {targetUser.UID}'s premium membership.\n" +
                                        $"New Expiration Date: {newExpiry}";

            if (newExpiry < DateTime.Now)
            {
                responseMessage += "\n⚠️ The new expiration date is in the past!";
            }

            await t.SendMessageAsync(
                text: responseMessage,
                replyToMessage: message
            );
        }
    }
}
