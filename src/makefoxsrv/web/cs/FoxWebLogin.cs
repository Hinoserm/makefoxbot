using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        [HttpGet("telegram")]
        public async Task<IActionResult> TelegramLogin([FromQuery] TelegramLoginModel model)
        {
            var token = FoxMain.settings.TelegramBotToken;

            try
            {
                ValidateTelegramLoginData(model, token);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            var user = await FoxUser.GetByTelegramUserID(model.id);

            if (user == null)
            {
                return Unauthorized("You are not currently a user of makefoxbot. Please start a conversation with the bot on Telegram and send /start before logging in here.");
            }

            if (user.GetAccessLevel() == AccessLevel.BANNED)
            {
                return Forbid("You have been banned from using this service.");
            }

            HttpContext.Session.SetString("UserSession", user.UID.ToString());

            return Redirect("/bu1ms");
        }

        private void ValidateTelegramLoginData(TelegramLoginModel model, string token)
        {
            var info = new Dictionary<string, string>
            {
                { "auth_date", model.auth_date.ToString() },
                { "first_name", model.first_name },
                { "id", model.id.ToString() },
                { "last_name", model.last_name },
                { "photo_url", model.photo_url },
                { "username", model.username }
            };

            var dataString = CombineString(info);
            var computedHash = HashHMAC(dataString, token);

            if (computedHash.ToLower() != model.hash)
            {
                throw new Exception("Data is NOT from Telegram");
            }

            if ((DateTime.UtcNow - DateTimeOffset.FromUnixTimeSeconds(model.auth_date)).TotalSeconds > 86400)
            {
                throw new Exception("Data is outdated");
            }
        }

        private string CombineString(IReadOnlyDictionary<string, string> meta)
        {
            var builder = new StringBuilder();

            TryAppend("auth_date");
            TryAppend("first_name");
            TryAppend("id");
            TryAppend("last_name");
            TryAppend("photo_url");
            TryAppend("username", true);

            return builder.ToString();

            void TryAppend(string key, bool isLast = false)
            {
                if (meta.ContainsKey(key) && !string.IsNullOrEmpty(meta[key]))
                {
                    builder.Append($"{key}={meta[key]}{(isLast ? "" : "\n")}");
                }
            }
        }

        private string HashHMAC(string message, string botToken)
        {
            using var hasher = SHA256.Create();
            var keyBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(botToken));

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var hash = new HMACSHA256(keyBytes);
            var computedHash = hash.ComputeHash(messageBytes);
            return BitConverter.ToString(computedHash).Replace("-", "").ToLower();
        }
    }

    public class TelegramLoginModel
    {
        public long id { get; set; }
        public string? first_name { get; set; }
        public string? last_name { get; set; }
        public string? username { get; set; }
        public string? photo_url { get; set; }
        public long auth_date { get; set; }
        public string hash { get; set; }
    }
}
