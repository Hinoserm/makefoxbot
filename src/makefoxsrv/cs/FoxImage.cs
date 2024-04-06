using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;
using WTelegram;
using makefoxsrv;
using TL;
using System.IO;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using System.Linq.Expressions;
using SixLabors.Fonts.Unicode;

namespace makefoxsrv
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
        public long? TelegramChatID = null;
        public long? TelegramMessageID = null;
        public DateTime DateAdded = DateTime.MinValue;

        public byte[]? Image = null;

        public static async Task ConvertOldImages()
        {
            int count = 0;

            while (true)
            {
                try
                {
                    using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "SELECT id FROM images WHERE image_file IS NULL ORDER BY date_added DESC LIMIT 1000";

                        using var r = await cmd.ExecuteReaderAsync();

                        if (!r.HasRows)
                            break;

                        while (await r.ReadAsync())
                        {
                            try
                            {
                                long id = System.Convert.ToInt64(r["id"]);

                                var img = await FoxImage.Load((ulong)id);

                                await img.Save();

                                count++;

                                if (count % 100 == 0)
                                {
                                    FoxLog.WriteLine($"Converted {count} images.");
                                }
                            }
                            catch (Exception ex)
                            {
                                FoxLog.WriteLine($"Error converting image: {ex.Message}\r\n{ex.StackTrace}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Error converting images: {ex.Message}\r\n{ex.StackTrace}");
                }
            }
            FoxLog.WriteLine($"Finished converting {count} images.");
        }

        public static (int, int) NormalizeImageSize(int width, int height)
        {
            const int MaxWidthHeight = 1280;
            const int MinWidthHeight = 512;

            double aspectRatio = (double)width / height;

            // First adjust dimensions to not exceed the max limit while maintaining aspect ratio
            if (width > MaxWidthHeight || height > MaxWidthHeight)
            {
                if (aspectRatio >= 1) // Image is wider than it is tall
                {
                    width = MaxWidthHeight;
                    height = (int)(width / aspectRatio);
                }
                else // Image is taller than it is wide
                {
                    height = MaxWidthHeight;
                    width = (int)(height * aspectRatio);
                }
            }

            // Then ensure dimensions do not fall below the min limit
            if (width < MinWidthHeight || height < MinWidthHeight)
            {
                if (aspectRatio >= 1) // Image is wider than it is tall
                {
                    width = MinWidthHeight;
                    height = (int)(width / aspectRatio);
                }
                else // Image is taller than it is wide
                {
                    height = MinWidthHeight;
                    width = (int)(height * aspectRatio);
                }
            }

            // Ensure both dimensions are rounded up to the nearest multiple of 64, without exceeding the max limit
            width = RoundUpToNearestMultipleWithinLimit(width, 64, MaxWidthHeight);
            height = RoundUpToNearestMultipleWithinLimit(height, 64, MaxWidthHeight);

            return (width, height);
        }

        public static async Task<bool> IsImageValid(ulong imageID)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM images WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", imageID);

                var result = await cmd.ExecuteScalarAsync();

                return Convert.ToInt32(result) > 0;
            }
        }

        // Reads an image file and returns its content as a byte array
        public static byte[] ReadImageFromFile(string relativeImagePath)
        {
            // Compute the absolute path from the executable location and the relative path provided
            string fullPath = Path.GetFullPath(Path.Combine("../data", relativeImagePath));

            // Read all bytes from the image file
            byte[] imageData = File.ReadAllBytes(fullPath);
            return imageData;
        }

        // Writes a byte array to an image file
        public static void WriteImageToFile(byte[] imageData, string relativeImagePath)
        {
            // Compute the absolute path from the executable location and the relative path provided
            string fullPath = Path.GetFullPath(Path.Combine("../data", relativeImagePath));

            // Ensure the directory exists before writing the file
            string directoryPath = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Write all bytes to the image file
            File.WriteAllBytes(fullPath, imageData);
        }

        public static string GenerateImagePath(ImageType type, string sha1Checksum, DateTime creationTime, string fileExtension = "png")
        {
            // Convert the ImageType enum to lowercase string, handling specific cases as needed
            string typePath = type.ToString().ToLower();

            // Handle specific directory names for input and output, others can be added as needed
            if (type == ImageType.INPUT)
            {
                typePath = "input";
            }
            else if (type == ImageType.OUTPUT)
            {
                typePath = "output";
            }

            // Format the month name in lowercase
            string monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(creationTime.Month).ToLower();

            // Construct the file path
            string filePath = $"images/{typePath}/{creationTime.Year}/{monthName}/{creationTime.ToString("dd")}/{creationTime.ToString("HH")}/{sha1Checksum.ToUpper()}.{fileExtension}";

            return filePath;
        }

        private static int RoundUpToNearestMultipleWithinLimit(int value, int multiple, int limit)
        {
            int roundedValue = ((value + multiple - 1) / multiple) * multiple;
            return Math.Min(roundedValue, limit);
        }

        public static async Task<FoxImage> Create(ulong user_id, byte[] image, ImageType type, string? filename = null, string ? tele_fileid = null, string? tele_uniqueid = null, long? tele_chatid = null, long? tele_msgid = null)
        {
            var img = new FoxImage();

            img.UserID = user_id;

            img.ID = await img.Save(type, image, filename, tele_fileid, tele_uniqueid, tele_chatid, tele_msgid);

            return img;
        }

        public static string GetImageExtension(byte[] imageData)
        {
            // Attempt to detect the format of the image
            var format = SixLabors.ImageSharp.Image.DetectFormat(imageData);

            if (format is null)
            {
                throw new ArgumentException("Unable to determine image format", nameof(imageData));
            }

            // Return the appropriate file extension based on the detected format
            return format.FileExtensions.FirstOrDefault() ?? throw new InvalidOperationException("Format detected, but no extension found");
        }

        public async Task<ulong> Save(ImageType? type = null, byte[]? image = null, string? filename = null, string? tele_fileid = null, string? tele_uniqueid = null, long? tele_chatid = null, long? tele_msgid = null)
        {
            if (type is not null)
                this.Type = type.Value;
            if (filename is not null)
                this.Filename = filename;
            if (tele_fileid is not null)
                this.TelegramFileID = tele_fileid;
            if (tele_uniqueid is not null)
                this.TelegramUniqueID = tele_uniqueid;
            if (tele_chatid is not null)
                this.TelegramChatID = tele_chatid;
            if (tele_msgid is not null)
                this.TelegramMessageID = tele_msgid;

            if (image is not null)
                this.Image = image;

            if (image is not null || SHA1Hash is null)
            {
                //If the image changed, or, if the hash is missing, regenerate it.
                this.SHA1Hash = sha1hash(image);
            }

            if (this.Image is null)
                throw new Exception("Image must not be null");

            if (this.DateAdded == DateTime.MinValue)
                this.DateAdded = DateTime.Now;

            var imagePath = GenerateImagePath(this.Type, this.SHA1Hash, this.DateAdded, GetImageExtension(this.Image));

            WriteImageToFile(this.Image, imagePath);


            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                        INSERT INTO images 
                            (id, type, user_id, filename, filesize, image, image_file, sha1hash, date_added, 
                             telegram_fileid, telegram_uniqueid, telegram_chatid, telegram_msgid) 
                        VALUES 
                            (@id, @type, @user_id, @filename, @filesize, @image, @image_file, @hash, @now, 
                             @tele_fileid, @tele_uniqueid, @tele_chatid, @tele_msgid)
                        ON DUPLICATE KEY UPDATE 
                            type = VALUES(type), 
                            user_id = VALUES(user_id), 
                            filename = VALUES(filename), 
                            filesize = VALUES(filesize),
                            image = VALUES(image),
                            image_file = VALUES(image_file), 
                            sha1hash = VALUES(sha1hash), 
                            date_added = VALUES(date_added), 
                            telegram_fileid = VALUES(telegram_fileid), 
                            telegram_uniqueid = VALUES(telegram_uniqueid), 
                            telegram_chatid = VALUES(telegram_chatid), 
                            telegram_msgid = VALUES(telegram_msgid);
                    ";

                    if (this.ID > 0)
                    {
                        cmd.Parameters.AddWithValue("id", this.ID);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("id", DBNull.Value);
                    }
                    cmd.Parameters.AddWithValue("type", this.Type.ToString());
                    cmd.Parameters.AddWithValue("user_id", this.UserID);
                    cmd.Parameters.AddWithValue("filename", this.Filename);
                    cmd.Parameters.AddWithValue("filesize", this.Image.LongLength);
                    cmd.Parameters.AddWithValue("image", "");
                    cmd.Parameters.AddWithValue("image_file", imagePath);
                    cmd.Parameters.AddWithValue("hash", this.SHA1Hash);
                    cmd.Parameters.AddWithValue("tele_fileid", this.TelegramFileID);
                    cmd.Parameters.AddWithValue("tele_uniqueid", this.TelegramUniqueID);
                    cmd.Parameters.AddWithValue("tele_chatid", this.TelegramChatID);
                    cmd.Parameters.AddWithValue("tele_msgid", this.TelegramMessageID);
                    cmd.Parameters.AddWithValue("now", this.DateAdded);

                    await cmd.ExecuteNonQueryAsync();

                    if (this.ID == 0)
                    {
                        this.ID = (ulong)cmd.LastInsertedId;
                    }

                    return this.ID;
                }
            }
        }

        public static async Task<FoxImage?> LoadFromTelegramUniqueId(ulong userId, string telegramUniqueID, long telegramChatID)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand("SELECT id FROM images WHERE user_id = @uid AND telegram_uniqueid = @id AND (telegram_chatid = @chatid OR telegram_chatid IS NULL) ORDER BY date_added DESC LIMIT 1", SQL))
            {
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("id", telegramUniqueID);
                cmd.Parameters.AddWithValue("chatid", telegramChatID);
                var result = await cmd.ExecuteScalarAsync();

                if (result is not null && result is not DBNull)
                    return await FoxImage.Load(Convert.ToUInt64(result));
            }

            return null;
        }

        public static async Task<FoxImage?> LoadLastUploaded(FoxUser user, long tele_chatid)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand("SELECT id FROM images WHERE user_id = @uid AND telegram_chatid = @chatid ORDER BY date_added DESC LIMIT 1", SQL))
            {
                cmd.Parameters.AddWithValue("uid", user.UID);
                cmd.Parameters.AddWithValue("chatid", tele_chatid);
                var result = await cmd.ExecuteScalarAsync();

                if (result is not null && result is not DBNull)
                    return await FoxImage.Load(Convert.ToUInt64(result));
            }

            return null;
        }

        public async Task SaveTelegramFileIds(string? telegramFileId = null, string? telegramUniqueId = null)
        {
            if (telegramFileId is not null)
                this.TelegramFileID = telegramFileId;

            if (telegramUniqueId is not null)
                this.TelegramUniqueID = telegramUniqueId;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

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

        public async Task SaveFullTelegramFileIds(string? telegramFileId = null, string? telegramUniqueId = null)
        {
            if (telegramFileId is not null)
                this.TelegramFullFileID = telegramFileId;

            if (telegramUniqueId is not null)
                this.TelegramFullUniqueID = telegramUniqueId;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

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

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

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
                    var image_file = r["image_file"];
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

                    if (image is null || image is DBNull || ((byte[])image).Length < 1)
                        if (image_file is null || image_file is DBNull)
                            throw new Exception("DB: Both 'image' and 'image_file' are null");
                        else
                            img.Image = ReadImageFromFile(Convert.ToString(image_file));
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
                    if (!(r["telegram_chatid"] is DBNull))
                        img.TelegramChatID = Convert.ToInt64(r["telegram_chatid"]);
                    if (!(r["telegram_msgid"] is DBNull))
                        img.TelegramMessageID = Convert.ToInt64(r["telegram_msgid"]);
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
        
        public static async Task SaveImageFromTelegram(FoxTelegram t, Message message, Photo photo)
        {
            try
            {
                FoxLog.WriteLine($"Got a photo from {t.User} ({message.ID})!");

                var user = await FoxUser.GetByTelegramUser(t.User, true);

                if (user is not null)
                {
                    await user.UpdateTimestamps();

                    MemoryStream memoryStream = new MemoryStream();

                    var fileType = await FoxTelegram.Client.DownloadFileAsync(photo, memoryStream, photo.LargestPhotoSize);
                    var fileName = $"{photo.id}.jpg";
                    if (fileType is not Storage_FileType.unknown and not Storage_FileType.partial)
                        fileName = $"{photo.id}.{fileType}";
                    //var fileHash = sha1hash(memoryStream.ToArray());

                    var newImg = await FoxImage.Create(user.UID, memoryStream.ToArray(), FoxImage.ImageType.INPUT, fileName, null, null, t.Chat is null ? null : t.Chat.ID, message.ID);

                    if (t.Chat is null) //Only save & notify outside of groups.
                    {
                        var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                        settings.selected_image = newImg.ID;

                        await settings.Save();

                        await t.SendMessageAsync(
                            text: "✅ Image saved and selected as input for /img2img",
                            replyToMessageId: message.ID
                        );
                    }
                    else if (t.Chat is not null)
                    {
                        // We need to check if the user replied to one of our messages or tagged us.

                        if (message.ReplyTo is not null && message.ReplyTo is MessageReplyHeader mrh)
                        {
                            long userId = 0;

                            switch (t.Chat)
                            {
                                case Channel channel:
                                    var rmsg = await FoxTelegram.Client.Channels_GetMessages(channel, new InputMessage[] { mrh.reply_to_msg_id });

                                    if (rmsg is not null && rmsg.Messages is not null)
                                        userId = rmsg.Messages.First().From;
                                    break;
                                case Chat chat:
                                    var crmsg = await FoxTelegram.Client.Messages_GetMessages(new InputMessage[] { mrh.reply_to_msg_id });

                                    if (crmsg is not null && crmsg.Messages is not null)
                                        userId = crmsg.Messages.First().From;
                                    break;
                            }

                            if (userId == FoxTelegram.Client.UserId)
                            {
                                var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                                settings.selected_image = newImg.ID;

                                await settings.Save();

                                await t.SendMessageAsync(
                                    text: "✅ Image saved as input for /img2img",
                                    replyToMessageId: message.ID
                                );
                              }
                        }
                        else if (message.entities is not null)
                        {
                            foreach (var entity in message.entities)
                            {
                                if (entity is MessageEntityMention)
                                {
                                    var username = message.message.Substring(entity.offset, entity.length);

                                    if (username == $"@{FoxTelegram.Client.User.username}")
                                    {
                                        var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                                        settings.selected_image = newImg.ID;

                                        await settings.Save();

                                        await t.SendMessageAsync(
                                            text: "✅ Image saved and selected as input for /img2img",
                                            replyToMessageId: message.ID
                                            );
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"Error with input image: {ex.Message}\r\n{ex.StackTrace}");
            }
        }
        public static (uint newWidth, uint newHeight) CalculateLimitedDimensions(uint originalWidth, uint originalHeight, uint maxWidthHeight = 768)
        {
            // If both dimensions are within the limit, return them as is.
            if (originalWidth <= maxWidthHeight && originalHeight <= maxWidthHeight)
            {
                return (originalWidth, originalHeight);
            }

            // Calculate aspect ratio
            double aspectRatio = (double)originalWidth / originalHeight;

            uint newWidth, newHeight;

            // If width is the larger dimension
            if (originalWidth >= originalHeight)
            {
                newWidth = maxWidthHeight;
                newHeight = (uint)(newWidth / aspectRatio);
            }
            else // Height is the larger dimension
            {
                newHeight = maxWidthHeight;
                newWidth = (uint)(newHeight * aspectRatio);
            }

            // Ensure new dimensions are not exceeding the limit (due to rounding issues)
            if (newWidth > maxWidthHeight)
            {
                newWidth = maxWidthHeight;
                newHeight = (uint)(newWidth / aspectRatio);
            }
            else if (newHeight > maxWidthHeight)
            {
                newHeight = maxWidthHeight;
                newWidth = (uint)(newHeight * aspectRatio);
            }

            return (newWidth, newHeight);
        }
    }
}
