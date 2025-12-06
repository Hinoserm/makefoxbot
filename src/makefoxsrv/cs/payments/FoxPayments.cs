using MySqlConnector;
using System;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;

namespace makefoxsrv
{
    public enum PaymentTypes
    {
        PAYPAL,
        STRIPE,
        TELEGRAM,
        OTHER
    }

    public class FoxPayments
    {
        public static int CalculateRewardDays(int amountInCents)
        {
            int baseDays = 30; // Base days for $10
            int targetDaysForMaxAmount = 365; // Target days for $100

            decimal amountInDollars = amountInCents / 100m;

            if (amountInCents == 500 || amountInCents < 10)
            {
                return 7; // Directly return 7 days for anything less than $10
            }
            else if (amountInDollars == 10)
            {
                return baseDays;
            }
            else if (amountInDollars >= 100)
            {
                decimal multiplesOverMax = amountInDollars / 100;
                return (int)Math.Round(targetDaysForMaxAmount * multiplesOverMax);
            }
            else
            {
                decimal daysPerDollar = (targetDaysForMaxAmount - baseDays) / (100m - 10m);
                return (int)Math.Round(baseDays + (amountInDollars - 10) * daysPerDollar);
            }
        }

        public class Invoice
        {
            public string UUID { get; private set; }
            public ulong UserId { get; set; }
            public int? Amount { get; set; } // In cents
            public int? Days { get; set; }
            public string? Currency { get; set; }
            public bool Recurring { get; set; }
            public DateTime DateCreated { get; private set; }
            public DateTime? DateCharged { get; set; }
            public DateTime? DateFailed { get; set; }
            public string? LastError { get; set; }

            public long? TelegramPeerId { get; set; }
            public long? TelegramMessageId { get; set; }

            protected Invoice(string uuid, ulong userId)
            {
                this.UUID = uuid;
                this.UserId = userId;
                this.DateCreated = DateTime.Now;
            }

            public static async Task<Invoice?> GetByUUID(string uuid)
            {
                Invoice? invoice = null;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM pay_invoices WHERE uuid = @uuid";
                    cmd.Parameters.AddWithValue("uuid", uuid);

                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        invoice = new Invoice(reader["uuid"].ToString()!, Convert.ToUInt64(reader["user_id"]))
                        {
                            DateCreated = Convert.ToDateTime(reader["date_created"]),
                            Amount = reader["amount"] as int?,
                            Currency = reader["currency"] as string ?? "USD",
                            Days = reader["days"] as int?,
                            Recurring = reader["recurring"] as bool? ?? false,
                            DateCharged = reader["date_charged"] as DateTime?,
                            DateFailed = reader["date_last_failed"] as DateTime?,
                            LastError = reader["last_error"] as string ?? null,
                            TelegramPeerId = reader["tg_peer_id"] as long?,
                            TelegramMessageId = reader["tg_msg_id"] as long?
                        };
                    }
                }

                return invoice;
            }

            public static async Task<Invoice> Create(FoxUser user)
            {
                var invoice = new Invoice(Guid.NewGuid().ToString(), user.UID);

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO pay_invoices (uuid, user_id, date_created) VALUES (@uuid, @user_id, @now)";
                    cmd.Parameters.AddWithValue("uuid", invoice.UUID);
                    cmd.Parameters.AddWithValue("user_id", user.UID);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }

                return invoice;
            }

            public async Task Save()
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;

                    cmd.CommandText = @"
                UPDATE pay_invoices 
                SET 
                    user_id = @user_id,
                    date_charged = @date_charged,
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
                    cmd.Parameters.AddWithValue("date_charged", this.DateCharged ?? (object)DBNull.Value);
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
        }


        public enum ChargeStatus
        {
            PENDING,
            COMPLETED,
            FAILED
        }

        public abstract class Charge
        {
            public Invoice Invoice { get; private set; }
            public long ChargeId { get; private set; }
            public string? ProviderToken { get; protected set; }
            public string? ProviderOrderId { get; protected set;}
            public ChargeStatus Status { get; protected set; }
            public DateTime DateCreated { get; private set; }
            public DateTime? DateCompleted { get; protected set; }
            public string? ErrorMessage { get; protected set; }
            public PaymentTypes Provider { get; protected set; }
            public ulong UserId => Invoice.UserId;

            internal Charge(Invoice invoice)
            {
                Invoice = invoice;
                DateCreated = DateTime.Now;
                Status = ChargeStatus.PENDING;
            }

            public async Task Process(string providerToken)
            {
                // Common code to be executed before the provider-specific processing
                if (Invoice.Amount is null)
                    throw new Exception("Amount is not set.");
                if (Invoice.Currency is null)
                    throw new Exception("Currency is not set.");
                if (Invoice.Days is null)
                    throw new Exception("Number of days is not set.");

                this.ProviderToken = providerToken;

                // Call the provider-specific processing step
                try
                {
                    await ProcessProviderSpecific(providerToken);

                    DateCompleted = Invoice.DateCharged = DateTime.Now;
                    Status = FoxPayments.ChargeStatus.COMPLETED;

                    // Common code to be executed after the provider-specific processing
                    await this.Save();
                    await Invoice.Save();

                    var user = await FoxUser.GetByUID(this.UserId);

                    if (user is null)
                        throw new Exception("Unable to load user.");

                    await user.RecordPayment(this.Provider, this.Invoice.Amount.Value, this.Invoice.Currency, this.Invoice.Days.Value, null, null, this.ProviderOrderId);

                    FoxLog.WriteLine($"Payment recorded for user {user.UID}: ({this.Invoice.Amount.Value}, \"{this.Invoice.Currency}\", {this.Invoice.Days.Value})");

                    try
                    {
                        if (this.Invoice.TelegramPeerId is not null && this.Invoice.TelegramMessageId is not null)
                        {
                            var teleUser = user.TelegramID is not null ? await FoxTelegram.GetUserFromID(user.TelegramID.Value) : null;
                            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

                            if (t is not null)
                            {
                                await t.DeleteMessage((int)this.Invoice.TelegramMessageId.Value);
                            }
                        }
                    }
                    catch
                    {
                        // Don't care if Telegram message deletion fails
                    }
                }
                catch (Exception ex)
                {
                    DateCompleted = Invoice.DateFailed = DateTime.Now;
                    this.Status = ChargeStatus.FAILED;
                    this.ErrorMessage = ex.Message;
                    Invoice.LastError = ex.Message;
                    await this.Save();
                    await Invoice.Save();

                    throw;
                }
            }

            protected abstract Task ProcessProviderSpecific(string providerToken);

            public static Charge Create(Invoice invoice, PaymentTypes provider)
            {
                FoxPayments.Charge charge = provider switch
                {
                    PaymentTypes.STRIPE => new FoxStripe.Charge(invoice),
                    PaymentTypes.PAYPAL => new FoxPayPal.Charge(invoice),
                    _ => throw new Exception("Unsupported payment provider.")
                };

                charge.Provider = provider;

                return charge;
            }

            public async Task Save()
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;

                    cmd.CommandText = @"
                    INSERT INTO pay_charges (invoice_id, user_id, provider, provider_token, provider_order_id, status, date_created, date_completed, error_message) 
                    VALUES (@invoice_id, @user_id, @provider, @provider_token, @provider_order_id, @status, @date_created, @date_completed, @error_message)
                    ON DUPLICATE KEY UPDATE
                    provider = VALUES(provider),
                    provider_token = VALUES(provider_token),
                    provider_order_id = VALUES(provider_order_id),
                    status = VALUES(status),
                    date_completed = VALUES(date_completed),
                    error_message = VALUES(error_message)";

                    cmd.Parameters.AddWithValue("invoice_id", this.Invoice.UUID);
                    cmd.Parameters.AddWithValue("user_id", this.UserId);
                    cmd.Parameters.AddWithValue("provider", this.Provider.ToString().ToUpperInvariant() ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("provider_token", this.ProviderToken ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("provider_order_id", this.ProviderOrderId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("status", this.Status.ToString());
                    cmd.Parameters.AddWithValue("date_created", this.DateCreated);
                    cmd.Parameters.AddWithValue("date_completed", this.DateCompleted ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("error_message", this.ErrorMessage ?? (object)DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();

                    if (this.ChargeId == 0)
                    {
                        this.ChargeId = Convert.ToInt64(cmd.LastInsertedId);
                    }
                }
            }

            public static async Task<List<Charge>> GetByUserId(ulong userId)
            {
                var charges = new List<Charge>();

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                    SELECT * 
                    FROM pay_charges
                    WHERE user_id = @user_id";
                    cmd.Parameters.AddWithValue("user_id", userId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        string provider = reader["provider"].ToString() ?? "OTHER";
                        string? chargeToken = reader["provider_token"].ToString() ?? null;
                        string? orderId = reader["provider_order_id"].ToString() ?? null;
                        string invoiceId = reader["invoice_id"].ToString() ?? string.Empty;

                        var invoice = await Invoice.GetByUUID(invoiceId);

                        FoxPayments.Charge? charge = provider switch
                        {
                            "STRIPE" => new FoxStripe.Charge(invoice)
                            {
                                ChargeId = Convert.ToInt64(reader["charge_id"]),
                                ProviderToken = chargeToken,
                                ProviderOrderId = orderId,
                                Status = Enum.Parse<ChargeStatus>(reader["status"].ToString()!),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateCompleted = reader["date_completed"] as DateTime?,
                                ErrorMessage = reader["error_message"].ToString()
                            },
                            "PAYPAL" => new FoxPayPal.Charge(invoice)
                            {
                                ChargeId = Convert.ToInt64(reader["charge_id"]),
                                ProviderToken = chargeToken,
                                ProviderOrderId = orderId,
                                Status = Enum.Parse<ChargeStatus>(reader["status"].ToString()!),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateCompleted = reader["date_completed"] as DateTime?,
                                ErrorMessage = reader["error_message"].ToString()
                            },
                            _ => null
                        };

                        if (charge is not null)
                        {
                            charges.Add(charge);
                        }
                    }
                }

                return charges;
            }

            public static async Task<Charge?> GetByID(long chargeId)
            {
                Charge? charge = null;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM pay_charges WHERE charge_id = @charge_id";
                    cmd.Parameters.AddWithValue("charge_id", chargeId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        string provider = reader["provider"].ToString() ?? "OTHER";
                        string? chargeToken = reader["provider_token"].ToString() ?? null;
                        string? orderId = reader["provider_order_id"].ToString() ?? null;
                        string invoiceId = reader["invoice_id"].ToString() ?? string.Empty;

                        var invoice = await Invoice.GetByUUID(invoiceId);

                        if (invoice is null)
                            throw new Exception($"Unable to load invoice {invoiceId}.");

                        charge = provider switch
                        {
                            "STRIPE" => new FoxStripe.Charge(invoice)
                            {
                                ChargeId = Convert.ToInt64(reader["charge_id"]),
                                ProviderToken = chargeToken,
                                ProviderOrderId = orderId,
                                Status = Enum.Parse<ChargeStatus>(reader["status"].ToString()!),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateCompleted = reader["date_completed"] as DateTime?,
                                ErrorMessage = reader["error_message"].ToString()
                            },
                            "PAYPAL" => new FoxPayPal.Charge(invoice)
                            {
                                ChargeId = Convert.ToInt64(reader["charge_id"]),
                                ProviderToken = chargeToken,
                                ProviderOrderId = orderId,
                                Status = Enum.Parse<ChargeStatus>(reader["status"].ToString()!),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateCompleted = reader["date_completed"] as DateTime?,
                                ErrorMessage = reader["error_message"].ToString()
                            },
                            _ => null
                        };
                    }
                }

                return charge;
            }
        }

        public enum SubscriptionStatus
        {
            ACTIVE,
            CANCELLED,
            PAUSED
        }

        public abstract class Subscription
        {
            public Invoice Invoice { get; private set; }
            public long SubscriptionId { get; private set; }
            public string? ProviderSubscriptionId { get; protected set; }
            public SubscriptionStatus Status { get; protected set; }
            public DateTime DateCreated { get; protected set; }
            public DateTime? DateUpdated { get; protected set; }
            public PaymentTypes Provider { get; protected set; }

            internal Subscription(Invoice invoice)
            {
                Invoice = invoice;
                DateCreated = DateTime.Now;
                Status = SubscriptionStatus.ACTIVE;
            }

            public abstract Task Process();

            public static Subscription Create(Invoice invoice, PaymentTypes provider)
            {
                Subscription subscription = provider switch
                {
                    PaymentTypes.STRIPE => new FoxStripe.Subscription(invoice),
                    PaymentTypes.PAYPAL => new FoxPayPal.Subscription(invoice),
                    _ => throw new Exception("Unsupported payment provider.")
                };

                subscription.Provider = provider;

                return subscription;
            }

            public async Task Save()
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;

                    cmd.CommandText = @"
                INSERT INTO pay_subscriptions (invoice_id, user_id, provider, subscription_token, amount, currency, interval_days, status, date_created, date_updated) 
                VALUES (@invoice_id, @user_id, @provider, @subscription_token, @amount, @currency, @interval_days, @status, @date_created, @date_updated)
                ON DUPLICATE KEY UPDATE
                provider = VALUES(provider),
                subscription_token = VALUES(subscription_token),
                amount = VALUES(amount),
                currency = VALUES(currency),
                interval_days = VALUES(interval_days),
                status = VALUES(status),
                date_updated = VALUES(date_updated)";

                    cmd.Parameters.AddWithValue("invoice_id", this.Invoice.UUID);
                    cmd.Parameters.AddWithValue("user_id", this.Invoice.UserId);
                    cmd.Parameters.AddWithValue("provider", this.Provider.ToString().ToUpperInvariant() ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("subscription_token", this.ProviderSubscriptionId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("amount", this.Invoice.Amount ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("currency", this.Invoice.Currency ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("interval_days", this.Invoice.Days ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("status", this.Status.ToString());
                    cmd.Parameters.AddWithValue("date_created", this.DateCreated);
                    cmd.Parameters.AddWithValue("date_updated", this.DateUpdated ?? (object)DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();

                    if (this.SubscriptionId == 0)
                    {
                        this.SubscriptionId = Convert.ToInt64(cmd.LastInsertedId);
                    }
                }
            }

            public static async Task<List<Subscription>> GetByUserId(ulong userId)
            {
                var subscriptions = new List<Subscription>();

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                SELECT * 
                FROM pay_subscriptions
                WHERE user_id = @user_id";
                    cmd.Parameters.AddWithValue("user_id", userId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        string provider = reader["provider"].ToString() ?? "OTHER";
                        string subscriptionToken = reader["subscription_token"].ToString() ?? string.Empty;
                        string invoiceId = reader["invoice_id"].ToString() ?? string.Empty;

                        var invoice = await Invoice.GetByUUID(invoiceId);

                        if (invoice is null)
                            throw new Exception($"Unable to load invoice {invoiceId}.");

                        FoxPayments.Subscription? subscription = provider switch
                        {
                            "STRIPE" => new FoxStripe.Subscription(invoice)
                            {
                                SubscriptionId = Convert.ToInt64(reader["subscription_id"]),
                                ProviderSubscriptionId = subscriptionToken,
                                Status = Enum.Parse<SubscriptionStatus>(reader["status"].ToString()),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateUpdated = reader["date_updated"] as DateTime?
                            },
                            "PAYPAL" => new FoxPayPal.Subscription(invoice)
                            {
                                SubscriptionId = Convert.ToInt64(reader["subscription_id"]),
                                ProviderSubscriptionId = subscriptionToken,
                                Status = Enum.Parse<SubscriptionStatus>(reader["status"].ToString()),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateUpdated = reader["date_updated"] as DateTime?
                            },
                            _ => null
                        };

                        if (subscription is not null)
                        {
                            subscriptions.Add(subscription);
                        }
                    }
                }

                return subscriptions;
            }

            public static async Task<Subscription?> GetByID(long subscriptionId)
            {
                Subscription? subscription = null;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using var cmd = new MySqlCommand();
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM pay_subscriptions WHERE subscription_id = @subscription_id";
                    cmd.Parameters.AddWithValue("subscription_id", subscriptionId);

                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        string provider = reader["provider"].ToString() ?? "OTHER";
                        string subscriptionToken = reader["subscription_token"].ToString() ?? string.Empty;
                        string invoiceId = reader["invoice_id"].ToString() ?? string.Empty;

                        var invoice = await Invoice.GetByUUID(invoiceId);

                        subscription = provider switch
                        {
                            "STRIPE" => new FoxStripe.Subscription(invoice)
                            {
                                SubscriptionId = Convert.ToInt64(reader["subscription_id"]),
                                ProviderSubscriptionId = subscriptionToken,
                                Status = Enum.Parse<SubscriptionStatus>(reader["status"].ToString()),
                                DateCreated = Convert.ToDateTime(reader["date_created"]),
                                DateUpdated = reader["date_updated"] as DateTime?
                            },
                            // "PAYPAL" => new FoxPayPal.Subscription(invoice)
                            // {
                            //     SubscriptionId = Convert.ToInt64(reader["subscription_id"]),
                            //     ProviderSubscriptionId = subscriptionToken,
                            //     Status = Enum.Parse<SubscriptionStatus>(reader["status"].ToString()),
                            //     DateCreated = Convert.ToDateTime(reader["date_created"]),
                            //     DateUpdated = reader["date_updated"] as DateTime?
                            // },
                            _ => null
                        };
                    }
                }

                return subscription;
            }
        }
    }
}
