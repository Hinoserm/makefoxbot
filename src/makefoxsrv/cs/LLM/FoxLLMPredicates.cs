using MySqlConnector;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static makefoxsrv.FoxModel;

namespace makefoxsrv
{
    internal class FoxLLMPredicates
    {
        public static async Task<int> GetUserDailyLLMCount(FoxUser user)
        {
            using var sql = new MySqlConnection(FoxMain.sqlConnectionString);
            await sql.OpenAsync();

            using var cmd = new MySqlCommand(@"
                SELECT COUNT(*) 
                FROM llm_stats
                WHERE user_id = @uid
                  AND created_at >= CURDATE()
                  AND is_free = 0
            ", sql);

            cmd.Parameters.AddWithValue("@uid", user.UID);

            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        public static async Task<int> GetUserWeeklyLLMCount(FoxUser user)
        {
            using var sql = new MySqlConnection(FoxMain.sqlConnectionString);
            await sql.OpenAsync();

            using var cmd = new MySqlCommand(@"
                SELECT COUNT(*) 
                FROM llm_stats
                WHERE user_id = @uid
                  AND YEARWEEK(created_at, 1) = YEARWEEK(CURDATE(), 1)
                  AND is_free = 0
            ", sql);

            cmd.Parameters.AddWithValue("@uid", user.UID);

            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        public static async Task<LimitCheckResult> IsUserAllowedLLM(FoxUser user)
        {
            const int dailyLimit = 20;
            const int weeklyLimit = 100;

            if (user.CheckAccessLevel(AccessLevel.PREMIUM))
                return new(true, DenyReason.None, 0, 0);

            var reason = DenyReason.None;

            var daily = await GetUserDailyLLMCount(user);
            var weekly = await GetUserWeeklyLLMCount(user);

            if (daily >= dailyLimit)
                reason |= DenyReason.DailyLimitReached;

            if (weekly >= weeklyLimit)
                reason |= DenyReason.WeeklyLimitReached;

            return new(reason == DenyReason.None, reason, dailyLimit, weeklyLimit);
        }

        public static async Task<(int remainingDaily, int remainingWeekly)> GetRemainingLLMMessages(FoxUser user)
        {
            const int dailyLimit = 20;
            const int weeklyLimit = 100;

            //if (user.CheckAccessLevel(AccessLevel.PREMIUM))
            //    return (int.MaxValue, int.MaxValue); // Premium users effectively unlimited

            var daily = await GetUserDailyLLMCount(user);
            var weekly = await GetUserWeeklyLLMCount(user);

            var remainingDaily = Math.Max(0, dailyLimit - daily);
            var remainingWeekly = Math.Max(0, weeklyLimit - weekly);

            return (remainingDaily, remainingWeekly);
        }

    }
}
