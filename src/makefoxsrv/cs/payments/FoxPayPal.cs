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
using PayPalCheckoutSdk.Payments;
using static makefoxsrv.FoxPayments.Charge;

namespace makefoxsrv
{
    internal class FoxPayPal
    {
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

                var client = PayPalClient();
                var request = new AuthorizationsCaptureRequest(providerToken);

                request.RequestBody(new CaptureRequest());

                HttpResponse response = await client.Execute(request);
                var capturedOrder = response.Result<PayPalCheckoutSdk.Payments.Capture>();

                if (capturedOrder.Status == "COMPLETED" || capturedOrder.Status == "PENDING")
                {
                    ProviderOrderId = capturedOrder.Id;
                }
                else
                {
                    throw new Exception($"Payment capture failed. Status: {capturedOrder.Status}");
                }

                await Invoice.Save();
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

        // Placeholder for Subscription class, to throw NotImplementedException
        public class Subscription : FoxPayments.Subscription
        {
            internal Subscription(FoxPayments.Invoice invoice) : base(invoice)
            {
            }

            public override Task Process()
            {
                throw new NotImplementedException("PayPal subscriptions are not implemented yet.");
            }
        }
    }
}
