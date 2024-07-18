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

namespace makefoxsrv
{
    public class FoxWebPayment
    {
        [WebFunctionName("ProcessToken")]
        [WebLoginRequired(false)]
        public static async Task<JsonObject?> ProcessToken(FoxWebSession session, JsonObject jsonMessage)
        {
            string stripeToken = FoxJsonHelper.GetString(jsonMessage, "Token", false);
            long uid = FoxJsonHelper.GetLong(jsonMessage, "UID", false).Value;
            int price = FoxJsonHelper.GetInt(jsonMessage, "Price", false).Value;
            int days = FoxJsonHelper.GetInt(jsonMessage, "Days", false).Value;
            
            var user = await FoxUser.GetByUID(uid);

            if (user is null)
            {
                return new JsonObject
                {
                    ["Command"] = "Payments:ProcessToken",
                    ["Success"] = false,
                    ["Error"] = "Invalid UID."
                };
            }

            var options = new ChargeCreateOptions
            {
                Amount = price,
                Currency = "USD",
                Description = $"UID:{user.UID} Days:{days}",
                Source = stripeToken,
            };

            var service = new ChargeService();
            string? errorMessage;

            try
            {
                Charge charge = service.Create(options);

                if (charge.Captured && charge.Status == "succeeded")
                {
                    var response = new JsonObject
                    {
                        ["Command"] = "Payments:ProcessToken",
                        ["Success"] = true
                    };

                    await user.RecordPayment(PaymentTypes.STRIPE, price, "USD", days, null, null, charge.Id);


                    FoxLog.WriteLine($"Payment recorded for user {user.UID}: ({price}, \"USD\", {days})");

                    return response;
                }
                else
                {
                    // Payment failed
                    errorMessage = charge.FailureMessage;
                }
            }
            catch (StripeException ex)
            {
                // Handle error
                errorMessage = ex.Message;
            }

            return new JsonObject
            {
                ["Command"] = "Payments:ProcessToken",
                ["Success"] = false,
                ["Error"] = $"Payment failed: {errorMessage ?? "Reason Unknown"}"
            };
        }

        [WebFunctionName("ProcessPayPalOrder")]
        [WebLoginRequired(false)]
        public static async Task<JsonObject?> ProcessPayPalOrder(FoxWebSession session, JsonObject jsonMessage)
        {
            string payPalOrderId = FoxJsonHelper.GetString(jsonMessage, "OrderID", false);
            long uid = FoxJsonHelper.GetLong(jsonMessage, "UID", false).Value;
            int price = FoxJsonHelper.GetInt(jsonMessage, "Price", false).Value;
            int days = FoxJsonHelper.GetInt(jsonMessage, "Days", false).Value;

            var user = await FoxUser.GetByUID(uid);

            if (user is null)
            {
                return new JsonObject
                {
                    ["Command"] = "Payments:ProcessPayPalOrder",
                    ["Success"] = false,
                    ["Error"] = "Invalid UID."
                };
            }

            var client = PayPalClient();

            var request = new OrdersGetRequest(payPalOrderId);
            string? errorMessage;

            try
            {
                var ppResponse = await client.Execute(request);
                var order = ppResponse.Result<PayPalCheckoutSdk.Orders.Order>();

                if (order.Status == "COMPLETED")
                {
                    //var amount = decimal.Parse(order.PurchaseUnits[0].Amount.Value);
                    //var currency = order.PurchaseUnits[0].Amount.CurrencyCode;

                    //if (amount * 100 == price && currency == "USD")
                    //{
                        await user.RecordPayment(PaymentTypes.PAYPAL, price, "USD", days, null, null, order.Id);

                        var response = new JsonObject
                        {
                            ["Command"] = "Payments:ProcessPayPalOrder",
                            ["Success"] = true
                        };

                        FoxLog.WriteLine($"Payment recorded for user {user.UID}: ({price}, \"USD\", {days})");

                        return response;
                    //}
                    //else
                    //{
                    //    errorMessage = "Payment amount or currency mismatch.";
                    //}
                }
                else
                {
                    errorMessage = $"Payment not completed. Status: {order.Status}";
                }
            }
            catch (HttpException ex)
            {
                errorMessage = ex.Message;
            }

            return new JsonObject
            {
                ["Command"] = "Payments:ProcessPayPalOrder",
                ["Success"] = false,
                ["Error"] = $"Payment failed: {errorMessage ?? "Reason Unknown"}"
            };
        }

        private static PayPalHttpClient PayPalClient()
        {
            if (FoxMain.settings?.PayPalClientId is null || FoxMain.settings?.PayPalSecretKey is null)
                throw new Exception("PayPal credentials are not properly configured.");

            var environment = new LiveEnvironment(FoxMain.settings?.PayPalClientId, FoxMain.settings?.PayPalSecretKey);
            return new PayPalHttpClient(environment);
        }
    }
}
