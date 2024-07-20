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
using static makefoxsrv.FoxPayments.Charge;

namespace makefoxsrv
{
    internal class FoxStripe {
        public class Charge : FoxPayments.Charge
        {
            internal Charge(FoxPayments.Invoice invoice) : base(invoice)
            {

            }

            protected override async Task ProcessProviderSpecific(string providerToken)
            {
                if (Invoice.Amount is null)
                    throw new Exception("Amount not set.");
                if (Invoice.Currency is null)
                    throw new Exception("Currency not set.");

                var options = new ChargeCreateOptions
                {
                    Amount = Invoice.Amount.Value,
                    Currency = Invoice.Currency,
                    Description = $"UID:{Invoice.UserId} Days:{Invoice.Days}",
                    Source = providerToken,
                };

                var service = new ChargeService();
                var charge = await service.CreateAsync(options);

                if (charge.Captured && charge.Status == "succeeded")
                {
                    ProviderOrderId = charge.Id;
                }
                else
                {
                    throw new Exception(charge.FailureMessage);
                }

                await Invoice.Save();
            }
        }

        public class Subscription : FoxPayments.Subscription
        {
            internal Subscription(FoxPayments.Invoice invoice) : base(invoice)
            {
            }

            public override async Task Process()
            {
                if (Invoice.Amount is null)
                    throw new Exception("Amount not set.");
                if (Invoice.Currency is null)
                    throw new Exception("Currency not set.");
                if (Invoice.Days is null)
                    throw new Exception("Interval days not set.");

                var customerService = new CustomerService();
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = Invoice.UserId.ToString()
                });

                var priceService = new PriceService();
                var price = await priceService.CreateAsync(new PriceCreateOptions
                {
                    UnitAmount = Invoice.Amount.Value,
                    Currency = Invoice.Currency,
                    Recurring = new PriceRecurringOptions
                    {
                        Interval = "day",
                        IntervalCount = Invoice.Days.Value
                    },
                    ProductData = new PriceProductDataOptions
                    {
                        Name = $"Subscription for User {Invoice.UserId}"
                    }
                });

                var subscriptionService = new SubscriptionService();
                var subscription = await subscriptionService.CreateAsync(new SubscriptionCreateOptions
                {
                    Customer = customer.Id,
                    Items = new List<SubscriptionItemOptions>
                    {
                        new SubscriptionItemOptions { Price = price.Id }
                    }
                });

                ProviderSubscriptionId = subscription.Id;
                Status = FoxPayments.SubscriptionStatus.ACTIVE;
                DateCreated = DateTime.Now;

                await Save();
            }
        }
    }
}
