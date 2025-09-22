using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;

namespace makefoxsrv
{
    class FoxPremium
    {
        public async Task<string[]?> CheckSettingsArePremium(FoxUserSettings settings)
        {
            // List to store premium features
            List<string> premiumFeatures = new List<string>();

            if (settings.Width > 1088)
                premiumFeatures.Add("width above 1088");

            if (settings.Height > 1088)
                premiumFeatures.Add("height above 1088");

            if (settings.steps > 20)
                premiumFeatures.Add("steps above 20");

            // Return the array of premium features if any, or null if none
            return premiumFeatures.Count > 0 ? premiumFeatures.ToArray() : null;
        }

        [Cron (hours: 1)]
        public async Task ProcessPremiumNotificationsAsync()
        {
            // Get current system time (always use local time)
            DateTime now = DateTime.Now;

            // First, load users with a premium expiration date into memory.
            var users = new List<(long id, DateTime expiry, DateTime? lastNotified)>();

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                // Load all users with a non-null premium expiration date
                using (var selectCmd = new MySqlCommand("SELECT id, date_premium_expires, last_premium_notification" +
                                                        " FROM users" +
                                                        " WHERE access_level != 'BANNED'" +
                                                        " AND date_premium_expires IS NOT NULL" +
                                                        " AND (last_premium_notification IS NULL OR last_premium_notification < @priorDay)" +
                                                        " AND date_premium_expires BETWEEN @minDate AND @maxDate",
                                                        connection)
                )
                {
                    selectCmd.Parameters.AddWithValue("@minDate", now.AddDays(-7));
                    selectCmd.Parameters.AddWithValue("@maxDate", now.AddDays(5));
                    selectCmd.Parameters.AddWithValue("@priorDay", now.AddDays(-1));

                    // then proceed with executing the reader...
                    using (var reader = await selectCmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            long id = reader.GetInt64("id");
                            DateTime expiry = reader.GetDateTime("date_premium_expires");
                            DateTime? lastNotified = reader.IsDBNull(reader.GetOrdinal("last_premium_notification"))
                                ? (DateTime?)null
                                : reader.GetDateTime("last_premium_notification");

                            users.Add((id, expiry, lastNotified));
                        }
                    }
                }

                if (users.Count == 0)
                    return; // Nothing to do.

                int sentCount = 0;
                int errorCount = 0;
                
                FoxLog.WriteLine($"Found {users.Count} users for premium expiry notifications. Processing...");

                // Process each user and determine if a notification should be sent.
                foreach (var user in users)
                {
                    string? message = null;

                    // If the membership has already expired
                    if (user.expiry <= now)
                    {
                        if (user.lastNotified == null || user.lastNotified < user.expiry)
                        {
                            if ((now - user.expiry).TotalDays >= 1)
                                message = $"Your paid /membership expired on {user.expiry:MMMM d yyyy}";
                            else
                                message = "Your paid /membership has expired.";
                        }
                    }
                    else
                    {
                        // Evaluate pre-expiration milestones in order of urgency.
                        // Check the 24-hour threshold first because that's most urgent.
                        if (now >= user.expiry.AddDays(-1))
                        {
                            if (user.lastNotified == null || user.lastNotified < user.expiry.AddDays(-1))
                            {
                                message = "Your paid /membership expires soon.";
                            }
                        }
                        else if (now >= user.expiry.AddDays(-2))
                        {
                            if (user.lastNotified == null || user.lastNotified < user.expiry.AddDays(-2))
                            {
                                message = "Your paid /membership expires in 2 days";
                            }
                        }
                        else if (now >= user.expiry.AddDays(-3))
                        {
                            if (user.lastNotified == null || user.lastNotified < user.expiry.AddDays(-3))
                            {
                                message = "Your paid /membership expires in 3 days";
                            }
                        }
                    }

                    if (message is not null)
                    {
                        var options = new[]
                        {
                            "Please consider renewing today.  Our survival depends on your support.",
                            "Please consider renewing today.  We can't do this without you.",
                            "Please renew today—your support keeps us alive.",
                            "Renew today. You're the reason we're still here.",
                            "Please renew now. Your support makes all the difference."
                        };

                        var random = new Random();
                        message += "\r\n\r\n" + options[random.Next(options.Length)];
                    }

                    // If a message is due, try to send it and then update the last notification timestamp
                    if (message != null)
                    {
                        try
                        {
                            // Locate and create user object
                            var fUser = await FoxUser.GetByUID((ulong)user.id);

                            if (fUser is null)
                                throw new Exception("User not found!");

                            if (fUser.GetAccessLevel() == AccessLevel.BANNED)
                                throw new Exception("User is banned!");

                            // Create Telegram object for user
                            var tg = fUser.Telegram;

                            // Attempt to send the message
                            await tg.SendMessageAsync(message);

                            sentCount++;

                            // Update the user's last_premium_notification column to now
                            using (var updateCmd = new MySqlCommand(
                                "UPDATE users SET last_premium_notification = @now WHERE id = @id", connection))
                            {
                                updateCmd.Parameters.AddWithValue("@now", now);
                                updateCmd.Parameters.AddWithValue("@id", user.id);
                                await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            FoxLog.LogException(ex);
                        }

                        await Task.Delay(10000); // Wait 10 seconds to avoid FLOOD_WAITs.
                    }
                }

                FoxLog.WriteLine($"Processed premium notifications: Sent {sentCount}, Errors {errorCount}");
            }
        }
    }
}
