using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Reflection.Metadata;
using System.Linq.Expressions;
using System.Drawing;
using MySqlConnector;
using WTelegram;
using TL;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.ComponentModel.Design;
using Stripe;
using Stripe.FinancialConnections;
using PayPalCheckoutSdk.Orders;
using System.Data;

namespace makefoxsrv
{
    internal class FoxGroupAdmin
    {
        private static async Task CmdGroupAdmin(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            // Get from the database all of the groups this user is an admin of.

            //using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            //await SQL.OpenAsync();

            //string query = @"SELECT * FROM telegram_chat_admins WHERE admin_id = @adminId";
            //FROM model_info 
            //             WHERE model_name = @modelName";
            //using var cmd = new MySqlCommand(query, SQL);
            //cmd.Parameters.AddWithValue("@modelName", Name);

            //using (var reader = await cmd.ExecuteReaderAsync())
            //{
            //    if (await reader.ReadAsync())
            //    {
            //        IsPremium = reader.GetBoolean("is_premium");

            //        // Handle nullable fields, assigning null if the value is DBNull
            //        Notes = reader.IsDBNull("notes") ? null : reader.GetString("notes");

            //    }
        }
    }
}
