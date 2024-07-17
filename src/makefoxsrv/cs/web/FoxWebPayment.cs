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

                    await user.RecordPayment(price, "USD", days, null, null, charge.Id);


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
    }
}
