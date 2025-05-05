using MySqlConnector;
using TL;

namespace makefoxsrv
{
    public class FoxUserSettings
    {
        private int? _steps;
        private decimal? _cfgScale;
        private string? _prompt;
        private string? _negativePrompt;
        private uint? _width;
        private uint? _height;
        private decimal? _denoisingStrength;
        private string? _modelName;
        private FoxModel? _model;
        private string? _sampler;

        public bool regionalPrompting = false;

        [DbColumn("steps")]
        public int steps
        {
            get => _steps ?? FoxSettings.Get<int>("DefaultSteps");
            set => _steps = value;
        }

        [DbColumn("cfgscale")]
        public decimal CFGScale
        {
            get => _cfgScale ?? FoxSettings.Get<decimal>("DefaultCFGScale");
            set => _cfgScale = value;
        }

        [DbColumn("prompt")]
        public string Prompt
        {
            get => _prompt ?? FoxSettings.Get<string?>("DefaultPrompt") ?? "";
            set => _prompt = value;
        }

        [DbColumn("negative_prompt")]
        public string NegativePrompt
        {
            get => _negativePrompt ?? FoxSettings.Get<string?>("DefaultNegative") ?? "";
            set => _negativePrompt = value;
        }

        [DbColumn("width")]
        public uint Width
        {
            get => _width ?? FoxSettings.Get<uint>("DefaultWidth");
            set => _width = value;
        }

        [DbColumn("height")]
        public uint Height
        {
            get => _height ?? FoxSettings.Get<uint>("DefaultHeight");
            set => _height = value;
        }

        [DbColumn("denoising_strength")]
        public decimal DenoisingStrength
        {
            get => _denoisingStrength ?? FoxSettings.Get<decimal>("DefaultDenoise");
            set => _denoisingStrength = value;
        }

        [DbColumn("model")]
        public string ModelName
        {
            get => _modelName ?? FoxSettings.Get<string>("DefaultModel")!;
            set => _modelName = value;
        }

        [DbColumn("sampler")]
        public string Sampler
        {
            get => _sampler ?? FoxSettings.Get<string>("DefaultSampler")!;
            set => _sampler = value;
        }

        [DbColumn("seed")]
        public int Seed = -1;

        [DbColumn("selected_image")]
        public ulong SelectedImage = 0;

        [DbColumn("hires_width")]
        public uint hires_width;

        [DbColumn("hires_height")]
        public uint hires_height;

        [DbColumn("hires_steps")]
        public uint hires_steps;

        [DbColumn("hires_denoising_strength")]
        public decimal hires_denoising_strength = 0.5M;

        [DbColumn("hires_enabled")]
        public bool hires_enabled = false;

        [DbColumn("hires_upscaler")]
        public string? hires_upscaler;

        [DbColumn("variation_seed")]
        public int? variation_seed = null;

        [DbColumn("variation_strength")]
        public decimal? variation_strength = null;

        public long TelegramUserID = 0;
        public long TelegramChatID = 0;

        private FoxUser? User;

        public FoxUserSettings Copy()
        {
            return new FoxUserSettings
            {
                _steps = this._steps,
                _cfgScale = this._cfgScale,
                _prompt = this._prompt,
                _negativePrompt = this._negativePrompt,
                _width = this._width,
                _height = this._height,
                _denoisingStrength = this._denoisingStrength,
                _modelName = this._modelName,
                _sampler = this._sampler,
                Seed = this.Seed,
                SelectedImage = this.SelectedImage,
                TelegramUserID = this.TelegramUserID,
                TelegramChatID = this.TelegramChatID,
                User = this.User,
                regionalPrompting = this.regionalPrompting,
                hires_width = this.hires_width,
                hires_height = this.hires_height,
                hires_steps = this.hires_steps,
                hires_denoising_strength = this.hires_denoising_strength,
                hires_enabled = this.hires_enabled,
                hires_upscaler = this.hires_upscaler,
                variation_seed = this.variation_seed,
                variation_strength = this.variation_strength
            };
        }

        public async Task Save()
        {
            if (User is null)
                throw new Exception("Can't save with NULL user object.");

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "REPLACE INTO telegram_user_settings (uid, tele_id, tele_chatid, steps, cfgscale, prompt, sampler, negative_prompt, selected_image, width, height, denoising_strength, seed, model) VALUES (@uid, @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @sampler, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed, @model)";
                    cmd.Parameters.AddWithValue("uid", User.UID);
                    cmd.Parameters.AddWithValue("tele_id", TelegramUserID);
                    cmd.Parameters.AddWithValue("tele_chatid", TelegramChatID);
                    cmd.Parameters.AddWithValue("steps", this._steps);
                    cmd.Parameters.AddWithValue("cfgscale", this._cfgScale);
                    cmd.Parameters.AddWithValue("prompt", this._prompt);
                    cmd.Parameters.AddWithValue("sampler", this._sampler);
                    cmd.Parameters.AddWithValue("negative_prompt", this._negativePrompt);
                    cmd.Parameters.AddWithValue("selected_image", this.SelectedImage);
                    cmd.Parameters.AddWithValue("width", this._width);
                    cmd.Parameters.AddWithValue("height", this._height);
                    cmd.Parameters.AddWithValue("denoising_strength", this._denoisingStrength);
                    cmd.Parameters.AddWithValue("seed", this.Seed);
                    cmd.Parameters.AddWithValue("model", this._modelName);

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

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
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
                                settings._cfgScale = Convert.ToDecimal(reader["cfgscale"]);
                            if (!(reader["prompt"] is DBNull))
                                settings._prompt = Convert.ToString(reader["prompt"]);
                            if (!(reader["negative_prompt"] is DBNull))
                                settings._negativePrompt = Convert.ToString(reader["negative_prompt"]);
                            if (!(reader["selected_image"] is DBNull))
                                settings.SelectedImage = Convert.ToUInt64(reader["selected_image"]);
                            if (!(reader["width"] is DBNull))
                                settings._width = Convert.ToUInt32(reader["width"]);
                            if (!(reader["height"] is DBNull))
                                settings._height = Convert.ToUInt32(reader["height"]);
                            if (!(reader["denoising_strength"] is DBNull))
                                settings._denoisingStrength = Convert.ToDecimal(reader["denoising_strength"]);
                            if (!(reader["seed"] is DBNull))
                                settings.Seed = Convert.ToInt32(reader["seed"]);
                            if (!(reader["model"] is DBNull))
                                settings._modelName = Convert.ToString(reader["model"]);
                            if (!(reader["sampler"] is DBNull))
                                settings._sampler = Convert.ToString(reader["sampler"]);
                        }
                    }
                }
                else
                {
                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = $"SELECT * FROM telegram_user_settings WHERE uid = {user.UID} AND tele_id = {tuser.ID} AND (tele_chatid = 0 OR tele_chatid IS NULL)";
                        using var reader = await cmd.ExecuteReaderAsync();
                        if (reader.HasRows && await reader.ReadAsync())
                        {
                            if (!(reader["steps"] is DBNull))
                                settings._steps = Convert.ToInt16(reader["steps"]);
                            if (!(reader["cfgscale"] is DBNull))
                                settings._cfgScale = Convert.ToDecimal(reader["cfgscale"]);
                            if (!(reader["prompt"] is DBNull))
                                settings._prompt = Convert.ToString(reader["prompt"]);
                            if (!(reader["negative_prompt"] is DBNull))
                                settings._negativePrompt = Convert.ToString(reader["negative_prompt"]);
                            if (!(reader["selected_image"] is DBNull))
                                settings.SelectedImage = Convert.ToUInt64(reader["selected_image"]);
                            if (!(reader["width"] is DBNull))
                                settings._width = Convert.ToUInt32(reader["width"]);
                            if (!(reader["height"] is DBNull))
                                settings._height = Convert.ToUInt32(reader["height"]);
                            if (!(reader["denoising_strength"] is DBNull))
                                settings._denoisingStrength = Convert.ToDecimal(reader["denoising_strength"]);
                            if (!(reader["seed"] is DBNull))
                                settings.Seed = Convert.ToInt32(reader["seed"]);
                            if (!(reader["model"] is DBNull))
                                settings._modelName = Convert.ToString(reader["model"]);
                            if (!(reader["sampler"] is DBNull))
                                settings._sampler = Convert.ToString(reader["sampler"]);
                        }
                    }
                }
            }

            return settings;
        }
    }
}
