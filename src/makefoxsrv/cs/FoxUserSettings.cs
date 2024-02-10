using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace makefoxbot
{
    internal class FoxUserSettings
    {
        //private MySqlConnection? SQL = null;

        public int steps = 20;
        public decimal cfgscale = 7.5M;
        public string prompt = "";
        public string negative_prompt = "";
        public ulong selected_image = 0;
        public uint width = 768;
        public uint height = 768;
        public decimal denoising_strength = 0.75M;
        public int seed = -1;

        public long TelegramUserID = 0;
        public long TelegramChatID = 0;

        private FoxUser? User;

        public async Task Save()
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "REPLACE INTO telegram_user_settings (uid, tele_id, tele_chatid, steps, cfgscale, prompt, negative_prompt, selected_image, width, height, denoising_strength, seed) VALUES (@uid, @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed)";
                    cmd.Parameters.AddWithValue("uid", User.UID);
                    cmd.Parameters.AddWithValue("tele_id", TelegramUserID);
                    cmd.Parameters.AddWithValue("tele_chatid", TelegramChatID);
                    cmd.Parameters.AddWithValue("steps", this.steps);
                    cmd.Parameters.AddWithValue("cfgscale", this.cfgscale);
                    cmd.Parameters.AddWithValue("prompt", this.prompt);
                    cmd.Parameters.AddWithValue("negative_prompt", this.negative_prompt);
                    cmd.Parameters.AddWithValue("selected_image", this.selected_image);
                    cmd.Parameters.AddWithValue("width", this.width);
                    cmd.Parameters.AddWithValue("height", this.height);
                    cmd.Parameters.AddWithValue("denoising_strength", this.denoising_strength);
                    cmd.Parameters.AddWithValue("seed", this.seed);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public static async Task<FoxUserSettings> GetTelegramSettings(FoxUser user, User? tuser, Chat? tchat = null)
        {
            if (tuser is null)
                throw new Exception("Can't continue with NULL user object.");

            var settings = new FoxUserSettings();

            settings.TelegramUserID = tuser.Id;
            settings.User = user;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                if (tchat is not null)
                {
                    settings.TelegramChatID = tchat.Id;

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.Id} AND tele_chatid = {tchat.Id}";
                        await using var reader = await cmd.ExecuteReaderAsync();
                        if (reader.HasRows && await reader.ReadAsync())
                        {
                            if (!(reader["steps"] is DBNull))
                                settings.steps = Convert.ToInt16(reader["steps"]);
                            if (!(reader["cfgscale"] is DBNull))
                                settings.cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                            if (!(reader["prompt"] is DBNull))
                                settings.prompt = Convert.ToString(reader["prompt"]);
                            if (!(reader["negative_prompt"] is DBNull))
                                settings.negative_prompt = Convert.ToString(reader["negative_prompt"]);
                            if (!(reader["selected_image"] is DBNull))
                                settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                            if (!(reader["width"] is DBNull))
                                settings.width = Convert.ToUInt32(reader["width"]);
                            if (!(reader["height"] is DBNull))
                                settings.height = Convert.ToUInt32(reader["height"]);
                            if (!(reader["denoising_strength"] is DBNull))
                                settings.denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                            if (!(reader["seed"] is DBNull))
                                settings.seed = Convert.ToInt32(reader["seed"]);

                            return settings;
                        }
                    }
                }

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.Id} AND tele_chatid = {tuser.Id}";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                    {
                        if (!(reader["steps"] is DBNull))
                            settings.steps = Convert.ToInt16(reader["steps"]);
                        if (!(reader["cfgscale"] is DBNull))
                            settings.cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                        if (!(reader["prompt"] is DBNull))
                            settings.prompt = Convert.ToString(reader["prompt"]);
                        if (!(reader["negative_prompt"] is DBNull))
                            settings.negative_prompt = Convert.ToString(reader["negative_prompt"]);
                        if (!(reader["selected_image"] is DBNull))
                            settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                        if (!(reader["width"] is DBNull))
                            settings.width = Convert.ToUInt32(reader["width"]);
                        if (!(reader["height"] is DBNull))
                            settings.height = Convert.ToUInt32(reader["height"]);
                        if (!(reader["denoising_strength"] is DBNull))
                            settings.denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                        if (!(reader["seed"] is DBNull))
                            settings.seed = Convert.ToInt32(reader["seed"]);

                        return settings;
                    }
                }

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.Id}";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                    {
                        if (!(reader["steps"] is DBNull))
                            settings.steps = Convert.ToInt16(reader["steps"]);
                        if (!(reader["cfgscale"] is DBNull))
                            settings.cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                        if (!(reader["prompt"] is DBNull))
                            settings.prompt = Convert.ToString(reader["prompt"]);
                        if (!(reader["negative_prompt"] is DBNull))
                            settings.negative_prompt = Convert.ToString(reader["negative_prompt"]);
                        if (!(reader["selected_image"] is DBNull))
                            settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                        if (!(reader["width"] is DBNull))
                            settings.width = Convert.ToUInt32(reader["width"]);
                        if (!(reader["height"] is DBNull))
                            settings.height = Convert.ToUInt32(reader["height"]);
                        if (!(reader["denoising_strength"] is DBNull))
                            settings.denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                        if (!(reader["seed"] is DBNull))
                            settings.seed = Convert.ToInt32(reader["seed"]);

                        return settings;
                    }
                }
            }

            return settings;
        }
    }
}
