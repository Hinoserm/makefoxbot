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
    public class FoxWebPayments
    {
        [WebFunctionName("CalcRewardDays")]
        [WebLoginRequired(false)]
        public static async Task<JsonObject?> CalcRewardDays(FoxWebSession session, JsonObject jsonMessage)
        {
            int amount = FoxJsonHelper.GetInt(jsonMessage, "Amount", false)!.Value;

            int rewardDays = FoxPayments.CalculateRewardDays(amount);

            var response = new JsonObject
            {
                ["Command"] = "Payments:CalcRewardDays",
                ["Success"] = true,
                ["Days"] = rewardDays
            };

            return response;
        }

        [WebFunctionName("Process")]
        [WebLoginRequired(false)]
        public static async Task<JsonObject?> Process(FoxWebSession session, JsonObject jsonMessage)
        {
            string chargeID = FoxJsonHelper.GetString(jsonMessage, "ChargeID", false)!;
            string sessionUUID = FoxJsonHelper.GetString(jsonMessage, "PaymentUUID", false)!;
            string provider = FoxJsonHelper.GetString(jsonMessage, "Provider", false)!;
            int amount = FoxJsonHelper.GetInt(jsonMessage, "Amount", false)!.Value;

            var pSession = await FoxPayments.Invoice.GetByUUID(sessionUUID);

            if (pSession is null)
                throw new Exception("Invalid payment session.");

            if (pSession.DateCharged is not null)
                throw new Exception("Payment session already charged.");

            pSession.Amount = amount;
            pSession.Days = FoxPayments.CalculateRewardDays(amount);

            PaymentTypes providerType;

            switch (provider.ToUpper())
            {
                case "STRIPE":
                    providerType = PaymentTypes.STRIPE;
                    break;
                case "PAYPAL":
                    providerType = PaymentTypes.PAYPAL;
                    break;
                default:
                    throw new Exception("Unknown payment provider.");
            }

            var charge = FoxPayments.Charge.Create(pSession, providerType);

            await charge.Process(chargeID);

            return new JsonObject
            {
                ["Command"] = "Payments:Process",
                ["Success"] = true,
            };
        }
    }
}
