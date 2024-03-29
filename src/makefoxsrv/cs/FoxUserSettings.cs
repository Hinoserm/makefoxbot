using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;
using TL;

namespace makefoxsrv
{
    internal class FoxUserSettings
    {
        private int? _steps;
        private decimal? _cfgscale;
        private string? _prompt;
        private string? _negative_prompt;
        private uint? _width;
        private uint? _height;
        private decimal? _denoising_strength;
        private string? _model;

        public int steps
        {
            get => _steps ?? FoxSettings.Get<int>("DefaultSteps");
            set => _steps = value;
        }

        public decimal cfgscale
        {
            get => _cfgscale ?? FoxSettings.Get<decimal>("DefaultCFGScale");
            set => _cfgscale = value;
        }

        public string? prompt
        {
            get => _prompt ?? FoxSettings.Get<string?>("DefaultPrompt");
            set => _prompt = value;
        }

        public string? negative_prompt
        {
            get => _negative_prompt ?? FoxSettings.Get<string?>("DefaultNegative");
            set => _negative_prompt = value;
        }

        public uint width
        {
            get => _width ?? FoxSettings.Get<uint>("DefaultWidth");
            set => _width = value;
        }

        public uint height
        {
            get => _height ?? FoxSettings.Get<uint>("DefaultHeight");
            set => _height = value;
        }

        public decimal denoising_strength
        {
            get => _denoising_strength ?? FoxSettings.Get<decimal>("DefaultDenoise");
            set => _denoising_strength = value;
        }

        public string? model
        {
            get => _model ?? FoxSettings.Get<string>("DefaultModel");
            set => _model = value;
        }

        public int seed = -1;
        
        public ulong selected_image = 0;

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
                    cmd.CommandText = "REPLACE INTO telegram_user_settings (uid, tele_id, tele_chatid, steps, cfgscale, prompt, negative_prompt, selected_image, width, height, denoising_strength, seed, model) VALUES (@uid, @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed, @model)";
                    cmd.Parameters.AddWithValue("uid", User.UID);
                    cmd.Parameters.AddWithValue("tele_id", TelegramUserID);
                    cmd.Parameters.AddWithValue("tele_chatid", TelegramChatID);
                    cmd.Parameters.AddWithValue("steps", this._steps);
                    cmd.Parameters.AddWithValue("cfgscale", this._cfgscale);
                    cmd.Parameters.AddWithValue("prompt", this._prompt);
                    cmd.Parameters.AddWithValue("negative_prompt", this._negative_prompt);
                    cmd.Parameters.AddWithValue("selected_image", this.selected_image);
                    cmd.Parameters.AddWithValue("width", this._width);
                    cmd.Parameters.AddWithValue("height", this._height);
                    cmd.Parameters.AddWithValue("denoising_strength", this._denoising_strength);
                    cmd.Parameters.AddWithValue("seed", this.seed);
                    cmd.Parameters.AddWithValue("model", this._model);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public static async Task<FoxUserSettings> GetTelegramSettings(FoxUser user, User? tuser, ChatBase? tchat = null)
        {
            if (tuser is null)
                throw new Exception("Can't continue with NULL user object.");

            var settings = new FoxUserSettings();

            settings.TelegramUserID = tuser.ID;
            settings.User = user;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                if (tchat is not null)
                {
                    settings.TelegramChatID = tchat.ID;

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.ID} AND tele_chatid = {tchat.ID}";
                        using var reader = await cmd.ExecuteReaderAsync();

                        if (reader.HasRows && await reader.ReadAsync())
                        {
                            if (!(reader["steps"] is DBNull))
                                settings._steps = Convert.ToInt16(reader["steps"]);
                            if (!(reader["cfgscale"] is DBNull))
                                settings._cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                            if (!(reader["prompt"] is DBNull))
                                settings._prompt = Convert.ToString(reader["prompt"]);
                            if (!(reader["negative_prompt"] is DBNull))
                                settings._negative_prompt = Convert.ToString(reader["negative_prompt"]);
                            if (!(reader["selected_image"] is DBNull))
                                settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                            if (!(reader["width"] is DBNull))
                                settings._width = Convert.ToUInt32(reader["width"]);
                            if (!(reader["height"] is DBNull))
                                settings._height = Convert.ToUInt32(reader["height"]);
                            if (!(reader["denoising_strength"] is DBNull))
                                settings._denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                            if (!(reader["seed"] is DBNull))
                                settings.seed = Convert.ToInt32(reader["seed"]);
                            if (!(reader["model"] is DBNull))
                                settings._model = Convert.ToString(reader["model"]);

                            return settings;
                        }
                    }
                }

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.ID} AND tele_chatid = 0";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                    {
                        if (!(reader["steps"] is DBNull))
                            settings._steps = Convert.ToInt16(reader["steps"]);
                        if (!(reader["cfgscale"] is DBNull))
                            settings._cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                        if (!(reader["prompt"] is DBNull))
                            settings._prompt = Convert.ToString(reader["prompt"]);
                        if (!(reader["negative_prompt"] is DBNull))
                            settings._negative_prompt = Convert.ToString(reader["negative_prompt"]);
                        if (!(reader["selected_image"] is DBNull))
                            settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                        if (!(reader["width"] is DBNull))
                            settings._width = Convert.ToUInt32(reader["width"]);
                        if (!(reader["height"] is DBNull))
                            settings._height = Convert.ToUInt32(reader["height"]);
                        if (!(reader["denoising_strength"] is DBNull))
                            settings._denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                        if (!(reader["seed"] is DBNull))
                            settings.seed = Convert.ToInt32(reader["seed"]);
                        if (!(reader["model"] is DBNull))
                            settings._model = Convert.ToString(reader["model"]);

                        return settings;
                    }
                }

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = 0";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                    {
                        if (!(reader["steps"] is DBNull))
                            settings._steps = Convert.ToInt16(reader["steps"]);
                        if (!(reader["cfgscale"] is DBNull))
                            settings._cfgscale = Convert.ToDecimal(reader["cfgscale"]);
                        if (!(reader["prompt"] is DBNull))
                            settings._prompt = Convert.ToString(reader["prompt"]);
                        if (!(reader["negative_prompt"] is DBNull))
                            settings._negative_prompt = Convert.ToString(reader["negative_prompt"]);
                        if (!(reader["selected_image"] is DBNull))
                            settings.selected_image = Convert.ToUInt64(reader["selected_image"]);
                        if (!(reader["width"] is DBNull))
                            settings._width = Convert.ToUInt32(reader["width"]);
                        if (!(reader["height"] is DBNull))
                            settings._height = Convert.ToUInt32(reader["height"]);
                        if (!(reader["denoising_strength"] is DBNull))
                            settings._denoising_strength = Convert.ToDecimal(reader["denoising_strength"]);
                        if (!(reader["seed"] is DBNull))
                            settings.seed = Convert.ToInt32(reader["seed"]);
                        if (!(reader["model"] is DBNull))
                            settings._model = Convert.ToString(reader["model"]); ;

                        return settings;
                    }
                }
            }

            return settings;
        }
    }
}
