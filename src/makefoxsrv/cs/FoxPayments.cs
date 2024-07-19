using EmbedIO;
using EmbedIO.WebSockets;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TL;
using System.Text.Json.Nodes;
using Stripe;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using System.CodeDom;
using Stripe.FinancialConnections;
using WTelegram;

using PayPalCheckoutSdk.Core;
using PayPalCheckoutSdk.Orders;
using PayPalHttp;
using Stripe.Forwarding;
using PayPalCheckoutSdk.Payments;

namespace makefoxsrv
{
    public enum PaymentTypes
    {
        PAYPAL,
        STRIPE,
        TELEGRAM,
        OTHER
    }

    internal class FoxPayments
    {
        public static int CalculateRewardDays(int amountInCents)
        {
            int baseDays = 30; // Base days for $10
            int targetDaysForMaxAmount = 365; // Target days for $100

            // Convert amount to dollars from cents for calculation
            decimal amountInDollars = amountInCents / 100m;

            if (amountInCents == 500)
            {
                return 7; // Directly return 7 days for $5
            }
            else if (amountInDollars <= 10)
            {
                return baseDays;
            }
            else if (amountInDollars >= 100)
            {
                // Calculate days as if $100 is given for each $100 increment
                decimal multiplesOverMax = amountInDollars / 100;
                return (int)Math.Round(targetDaysForMaxAmount * multiplesOverMax);
            }
            else
            {
                // Linear interpolation for amounts between $10 and $100
                decimal daysPerDollar = (targetDaysForMaxAmount - baseDays) / (100m - 10m);
                return (int)Math.Round(baseDays + (amountInDollars - 10) * daysPerDollar);
            }
        }

        public class Session
        {
            public string UUID { get; private set; }

            public ulong UserId { get; set; }

            public string? ChargeId { get ; set; }
            public string? OrderId { get; set; }

            public PaymentTypes? Provider { get; set; }

            public int? Amount { get; set; } // In cents
            public int? Days { get; set; }
            public string? Currency { get; set; }
            public bool Recurring { get; set; }

            public DateTime DateCreated { get; private set; }
            public DateTime? DateCharged { get; private set; }
            public DateTime? DateFailed { get; set; }

            public long? TelegramPeerId { get; set; }
            public long? TelegramMessageId { get; set; }

            public string? LastError { get; set; }

            private Session(string uuid, ulong userId)
            {
                this.UUID = uuid;
                this.UserId = userId;
            }

            public static async Task<Session?> GetByUUID(string uuid)
            {
                FoxPayments.Session? session = null;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();

                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM payment_sessions WHERE uuid = @uuid";
                    cmd.Parameters.AddWithValue("uuid", uuid);

                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        session = new Session(reader["uuid"].ToString()!, Convert.ToUInt64(reader["user_id"]));

                        session.DateCreated = Convert.ToDateTime(reader["date_created"]);
                        session.ChargeId = reader["provider_charge_id"] as string ?? null;
                        session.OrderId = reader["provider_order_id"] as string ?? null;
                        session.DateCharged = reader["date_charged"] as DateTime?;
                        session.Amount = reader["amount"] as int?;
                        session.Currency = reader["currency"] as string ?? "USD";
                        session.Days = reader["days"] as int?;
                        session.Recurring = reader["recurring"] as bool? ?? false;
                        session.DateFailed = reader["date_last_failed"] as DateTime?;
                        session.LastError = reader["last_error"] as string ?? null;
                        session.TelegramPeerId = reader["tg_peer_id"] as long?;
                        session.TelegramMessageId = reader["tg_msg_id"] as long?;

                        if (reader["provider"] != DBNull.Value)
                        {
                            session.Provider = (reader["provider"] as string)?.ToUpper() switch
                            {
                                "PAYPAL" => PaymentTypes.PAYPAL,
                                "STRIPE" => PaymentTypes.STRIPE,
                                _ => PaymentTypes.OTHER
                            };
                        }

                    }
                }

                return session;
            }

            public static async Task<Session> Create(FoxUser user)
            {
                var session = new Session(Guid.NewGuid().ToString(), user.UID);

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();

                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO payment_sessions (uuid, user_id, date_created) VALUES (@uuid, @user_id, @now)";
                    cmd.Parameters.AddWithValue("uuid", session.UUID);
                    cmd.Parameters.AddWithValue("user_id", user.UID);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }

                return session;
            }

            public async Task Save()
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;

                    cmd.CommandText = @"
                        UPDATE payment_sessions 
                        SET 
                            user_id = @user_id,
                            provider_charge_id = @charge_id,
                            provider_order_id = @order_id,
                            date_charged = @date_charged,
                            provider = @provider,
                            amount = @amount,
                            currency = @currency,
                            days = @days,
                            recurring = @recurring,
                            date_last_failed = @date_last_failed,
                            last_error = @last_error,
                            tg_peer_id = @tg_peer_id,
                            tg_msg_id = @tg_msg_id
                        WHERE 
                            uuid = @uuid";

                    cmd.Parameters.AddWithValue("uuid", this.UUID);
                    cmd.Parameters.AddWithValue("user_id", this.UserId);
                    cmd.Parameters.AddWithValue("charge_id", this.ChargeId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("order_id", this.OrderId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("date_charged", this.DateCharged ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("provider", this.Provider?.ToString().ToUpperInvariant() ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("amount", this.Amount ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("currency", this.Currency ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("days", this.Days ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("recurring", this.Recurring);
                    cmd.Parameters.AddWithValue("date_last_failed", this.DateFailed ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("last_error", this.LastError ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("tg_peer_id", this.TelegramPeerId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("tg_msg_id", this.TelegramMessageId ?? (object)DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            public async Task Charge()
            {
                if (this.Provider is null)
                    throw new Exception("Payment provider not set.");

                if (this.Amount is null)
                    throw new Exception("Payment amount not set.");

                if (this.Days is null)
                    throw new Exception("Payment days not set.");

                if (this.Currency is null)
                    throw new Exception("Payment currency not set.");

                if (this.DateCharged is not null)
                    throw new Exception("Payment already charged.");

                var user = await FoxUser.GetByUID((long)this.UserId);

                if (user is null)
                    throw new Exception("Invalid user.");

                string? successChargeId = null;

                switch (this.Provider)
                {
                    case PaymentTypes.STRIPE:
                        if (this.ChargeId is null)
                            throw new Exception("ChargeId not set.");

                        var options = new ChargeCreateOptions
                        {
                            Amount = this.Amount,
                            Currency = this.Currency,
                            Description = $"UID:{user.UID} Days:{this.Days}",
                            Source = this.ChargeId,
                        };

                        var service = new ChargeService();

                        Charge charge = service.Create(options);

                        if (charge.Captured && charge.Status == "succeeded")
                        {
                            this.DateCharged = DateTime.Now;
                            this.OrderId = charge.Id;
                        }
                        else
                        {
                            throw new Exception(charge.FailureMessage);
                        }

                        break;
                    case PaymentTypes.PAYPAL:

                        if (this.ChargeId is null)
                            throw new Exception("ChargeId not set.");

                        var client = PayPalClient();
                        var request = new AuthorizationsCaptureRequest(this.ChargeId);

                        request.RequestBody(new CaptureRequest());

                        HttpResponse response = await client.Execute(request);
                        var capturedOrder = response.Result<PayPalCheckoutSdk.Payments.Capture>();

                        if (capturedOrder.Status == "COMPLETED" || capturedOrder.Status == "PENDING")
                        {
                            this.OrderId = capturedOrder.InvoiceId;
                        }
                        else
                        {
                            throw new Exception($"Payment capture failed. Status: {capturedOrder.Status}");
                        }

                        break;
                    default:
                        throw new Exception("Unknown payment provider.");
                }

                await this.Save();

                await user.RecordPayment(this.Provider.Value, this.Amount.Value, this.Currency, this.Days.Value, null, null, successChargeId);

                FoxLog.WriteLine($"Payment recorded for user {user.UID}: ({this.Amount.Value}, \"{this.Currency}\", {this.Days.Value})");

                try
                {
                    if (this.TelegramPeerId is not null && this.TelegramMessageId is not null)
                    {
                        var teleUser = user.TelegramID is not null ? await FoxTelegram.GetUserFromID(user.TelegramID.Value) : null;
                        var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

                        if (t is not null)
                        {
                            await t.DeleteMessage((int)this.TelegramMessageId.Value);
                        }
                    }
                }
                catch
                {
                    // Don't care if Telegram message deletion fails
                }

            }

        }

        private static PayPalHttpClient PayPalClient()
        {
            if (FoxMain.settings?.PayPalClientId is null || FoxMain.settings?.PayPalSecretKey is null)
                throw new Exception("PayPal credentials are not properly configured.");

            if (FoxMain.settings.PayPalSandboxMode)
                return new PayPalHttpClient(new SandboxEnvironment(FoxMain.settings?.PayPalClientId, FoxMain.settings?.PayPalSecretKey));

            return new PayPalHttpClient(new LiveEnvironment(FoxMain.settings?.PayPalClientId, FoxMain.settings?.PayPalSecretKey));
        }

    }
}
