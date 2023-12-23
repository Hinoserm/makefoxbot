using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;
using Telegram.Bot.Types.ReplyMarkups;

namespace makefoxbot
{
    internal class FoxImage
    {
        public enum ImageType
        {
            INPUT,
            OUTPUT,
            OTHER,
            UNKNOWN
        }

        public ImageType Type = ImageType.UNKNOWN;

        public ulong ID;
        public ulong UserID;
        public string? Filename = null;
        public string? SHA1Hash = null;
        public string? TelegramFileID = null;
        public string? TelegramUniqueID = null;
        public string? TelegramFullFileID = null;
        public string? TelegramFullUniqueID = null;
        public DateTime DateAdded;

        public byte[] Image = null;

        public static async Task<FoxImage> Create(ulong user_id, byte[] image, ImageType type, string? filename = null, string ? tele_fileid = null, string? tele_uniqueid = null)
        {
            var img = new FoxImage();

            img.UserID = user_id;

            img.ID = await img.Save(type, image, filename, tele_fileid, tele_uniqueid);

            return img;
        }

        public async Task<ulong> Save(ImageType? type = null, byte[]? image = null, string? filename = null, string? tele_fileid = null, string? tele_uniqueid = null)
        {
            if (type is not null)
                this.Type = (ImageType)type;
            if (filename is not null)
                this.Filename = filename;
            if (image is not null)
                this.Image = image;
            if (tele_fileid is not null)
                this.TelegramFileID = tele_fileid;
            if (tele_uniqueid is not null)
                this.TelegramUniqueID = tele_uniqueid;

            this.SHA1Hash = sha1hash(this.Image);
            this.DateAdded = DateTime.Now;

            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO images (type, user_id, filename, filesize, image, sha1hash, date_added, telegram_fileid, telegram_uniqueid) VALUES (@type, @user_id, @filename, @filesize, @image, @hash, @now, @tele_fileid, @tele_uniqueid)";

                    cmd.Parameters.AddWithValue("type", this.Type.ToString());
                    cmd.Parameters.AddWithValue("user_id", this.UserID);
                    cmd.Parameters.AddWithValue("filename", this.Filename);
                    cmd.Parameters.AddWithValue("filesize", this.Image.LongLength);
                    cmd.Parameters.AddWithValue("image", this.Image);
                    cmd.Parameters.AddWithValue("hash", this.SHA1Hash);
                    cmd.Parameters.AddWithValue("tele_fileid", this.TelegramFileID);
                    cmd.Parameters.AddWithValue("tele_uniqueid", this.TelegramUniqueID);
                    cmd.Parameters.AddWithValue("now", this.DateAdded);

                    await cmd.ExecuteNonQueryAsync();

                    this.ID = (ulong)cmd.LastInsertedId;

                    return this.ID;
                }
            }
        }

        public static async Task<FoxImage?> LoadFromTelegramUniqueId(ulong userId, string telegramUniqueID)
        {
            using var SQL = new MySqlConnection(Program.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand("SELECT id FROM images WHERE user_id = @uid AND telegram_uniqueid = @id LIMIT 1", SQL))
            {
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("id", telegramUniqueID);
                var result = await cmd.ExecuteScalarAsync();

                if (result is not null && result is not DBNull)
                    return await FoxImage.Load(Convert.ToUInt64(result));
            }

            return null;
        }

        public async Task SaveTelegramFileIds(string telegramFileId = null, string telegramUniqueId = null)
        {
            if (telegramFileId is not null)
                this.TelegramFileID = telegramFileId;

            if (telegramUniqueId is not null)
                this.TelegramUniqueID = telegramUniqueId;

            using var SQL = new MySqlConnection(Program.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = $"UPDATE images SET telegram_fileid = @fileid, telegram_uniqueid = @uniqueid WHERE id = @id";
                cmd.Parameters.AddWithValue("id", this.ID);
                cmd.Parameters.AddWithValue("fileid", this.TelegramFileID);
                cmd.Parameters.AddWithValue("uniqueid", this.TelegramUniqueID);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task SaveFullTelegramFileIds(string telegramFileId = null, string telegramUniqueId = null)
        {
            if (telegramFileId is not null)
                this.TelegramFullFileID = telegramFileId;

            if (telegramUniqueId is not null)
                this.TelegramFullUniqueID = telegramUniqueId;

            using var SQL = new MySqlConnection(Program.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = $"UPDATE images SET telegram_full_fileid = @fileid, telegram_full_uniqueid = @uniqueid WHERE id = @id";
                cmd.Parameters.AddWithValue("id", this.ID);
                cmd.Parameters.AddWithValue("fileid", this.TelegramFullFileID);
                cmd.Parameters.AddWithValue("uniqueid", this.TelegramFullUniqueID);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<FoxImage?> Load(ulong image_id)
        {
            var img = new FoxImage();

            using var SQL = new MySqlConnection(Program.MySqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand("SELECT * FROM images WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", image_id);

                using var r = await cmd.ExecuteReaderAsync();
                if (r.HasRows && await r.ReadAsync())
                {
                    var userId = r["user_id"];
                    var type = r["type"];
                    var image = r["image"];
                    var sha1hash = r["sha1hash"];
                    var dateAdded = r["date_added"];

                    if (userId is null || userId is DBNull)
                        throw new Exception("DB: image.user_id must never be null");
                    else
                        img.UserID = Convert.ToUInt64(userId);

                    if (dateAdded is null || dateAdded is DBNull)
                        throw new Exception("DB: image.date_added must never be null");
                    else
                        img.DateAdded = Convert.ToDateTime(dateAdded);

                    if (type is null || type is DBNull)
                        throw new Exception("DB: image.type must never be null");
                    else
                        img.Type = (ImageType)Enum.Parse(typeof(ImageType), Convert.ToString(type) ?? "", true);

                    if (image is null || image is DBNull)
                        throw new Exception("DB: image.image must never be null");
                    else
                        img.Image = (byte[])image;

                    if (sha1hash is null || sha1hash is DBNull)
                        throw new Exception("DB: image.sha1hash must never be null");
                    else
                        img.SHA1Hash = Convert.ToString(sha1hash);  // Assuming Sha1Hash is the correct property name

                    if (!(r["telegram_fileid"] is DBNull))
                        img.TelegramFileID = Convert.ToString(r["telegram_fileid"]);
                    if (!(r["telegram_uniqueid"] is DBNull))
                        img.TelegramUniqueID = Convert.ToString(r["telegram_uniqueid"]);
                    if (!(r["telegram_full_fileid"] is DBNull))
                        img.TelegramFullFileID = Convert.ToString(r["telegram_full_fileid"]);
                    if (!(r["telegram_full_uniqueid"] is DBNull))
                        img.TelegramFullUniqueID = Convert.ToString(r["telegram_full_uniqueid"]);
                    if (!(r["filename"] is DBNull))
                        img.Filename = Convert.ToString(r["filename"]);
                    img.ID = image_id;

                    return img;
                }
            }

            return null;
        }

        private static string sha1hash(byte[] input)
        {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(input));
        }
    }
}
