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
using EmbedIO.Utilities;
using System.Security.Policy;
using SixLabors.ImageSharp.PixelFormats;

namespace makefoxsrv
{
    public class FoxImage
    {
        public enum ImageType
        {
            INPUT,
            OUTPUT,
            OTHER,
            UNKNOWN
        }

        [DbColumn("type")]
        public ImageType Type = ImageType.UNKNOWN;

        [DbColumn("id")]
        public ulong ID { get; private set; }

        [DbColumn("user_id")]
        public ulong UserID;

        [DbColumn("sha1hash")]
        public string? _sha1Hash = null;

        [DbColumn("date_added")]
        public DateTime DateAdded = DateTime.MinValue;

        [DbColumn("width")]
        private int? _width = null;

        [DbColumn("height")]
        private int? _height = null;

        [DbColumn("filename")]
        private string? _originalFilename = null;

        [DbColumn("filesize")]
        private long? _fileSize = null;

        [DbColumn("image_file")]
        private string _filePath = "";

        [DbColumn("hidden")]
        public bool Hidden = false;

        [DbColumn("flagged")]
        public bool Flagged = false;

        private byte[]? _imageDataRaw = null;

        private bool _isDirty = true; //Image was altered

        private Dictionary<string, float>? _imageTags = null;

        private readonly object _lock = new();

        public class TgInfo
        {
            [DbColumn("id")]
            private ulong _imageId;

            [DbColumn("telegram_chatid")]
            public long? TelegramChatID = null;
            [DbColumn("telegram_msgid")]
            public long? TelegramMessageID = null;
            [DbColumn("telegram_topicid")]
            public int? TelegramTopicID = null;
            [DbColumn("telegram_userid")]
            public long? TelegramUserID = null;

            public TgInfo() {

            }

            public TgInfo(FoxTelegram tg, Message? msg = null)
            {
                if (msg is not null)
                {
                    TelegramMessageID = msg?.ID;
                    TelegramTopicID = msg?.ReplyHeader?.TopicID ?? null;
                    TelegramUserID = msg?.From?.ID;
                }
                else
                {
                    TelegramUserID = tg?.User?.ID;
                }

                TelegramChatID = tg?.Chat?.ID;
            }

            public async Task Save(ulong ImageID)
            {
                this._imageId = ImageID;

                await FoxDB.SaveObjectAsync(this, "images_tg_info");
            }

            public static async Task<TgInfo?> Load(ulong ImageID)
            {
                var tgInfo = await FoxDB.LoadObjectAsync<TgInfo>("images_tg_info", "id = @id", new Dictionary<string, object?> { { "id", ImageID } });

                return tgInfo;
            }
        }

        private TgInfo? _telegramInfo = null;

        public async Task<TgInfo> GetTelegramInfoAsync()
        {
            if (_telegramInfo is null) {
                _telegramInfo = await TgInfo.Load(this.ID);
            }

            return _telegramInfo ?? new TgInfo();
        }

        class TgFileIds
        {
            [DbColumn("telegram_fileid")]
            public string? TelegramFileID = null;
            [DbColumn("telegram_uniqueid")]
            public string? TelegramUniqueID = null;
            [DbColumn("telegram_full_fileid")]
            public string? TelegramFullFileID = null;
            [DbColumn("telegram_full_uniqueid")]
            public string? TelegramFullUniqueID = null;
        }

        public string Filename
        {
            get => _originalFilename ?? "";
        }

        public int Width
        {
            get
            {
                PopulateDimensions();
                return _width ?? 0; // Default to 0 if somehow still null (should not happen).
            }
        }

        public string SHA1Hash
        {
            get
            {
                lock (_lock)
                {
                    if (_sha1Hash is null)
                        _sha1Hash = GenerateSHA1Hash(Image);

                    return _sha1Hash;
                }
            }
        }

        public int Height
        {
            get
            {
                PopulateDimensions();
                return _height ?? 0; // Default to 0 if somehow still null (should not happen).
            }
        }

        public byte[] Image
        {
            get
            {
                lock (_lock)
                {
                    if (_imageDataRaw is null)
                        ReadImageFromFile(); //Load image from disk

                    if (_imageDataRaw is null)
                        throw new InvalidOperationException("Image data is null");

                    return (byte[])_imageDataRaw.Clone(); // defensive copy
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_imageDataRaw is not null)
                        throw new Exception("Image data is already set. FoxImage instances are immutable once created.");

                    _imageDataRaw = (byte[])value.Clone();
                    _sha1Hash = GenerateSHA1Hash(_imageDataRaw);
                    _fileSize = _imageDataRaw.Length;
                    _filePath = GenerateImagePath();
                    PopulateDimensions();
                    _isDirty = true;
                }
            }
        }

        /// <summary>
        /// Loads a new Image<Rgba32> from raw data.
        /// Caller is responsible for disposing.
        /// </summary>
        public Image<Rgba32> GetRGBAImage()
        {
            lock (_lock)
            {
                if (this.Image is null)
                    throw new InvalidOperationException("Image data is null");

                return SixLabors.ImageSharp.Image.Load<Rgba32>(Image);
            }
        }

        private void PopulateDimensions()
        {
            lock (_lock)
            {
                if (_width is null || _height is null)
                {
                    var imgProperties = SixLabors.ImageSharp.Image.Identify(Image);

                    _width = imgProperties.Width;
                    _height = imgProperties.Height;
                }
            }
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

            using (var cmd = new MySqlCommand("SELECT COUNT(id) FROM images WHERE id = @id", SQL))
            {
                cmd.Parameters.AddWithValue("id", imageID);

                var result = await cmd.ExecuteScalarAsync();

                return Convert.ToInt32(result) > 0;
            }
        }

        // Reads an image file and returns its content as a byte array
        private void ReadImageFromFile()
        {
            lock (_lock)
            {
                // Compute the absolute path from the executable location and the relative path provided
                string fullPath = Path.GetFullPath(Path.Combine("../data", _filePath));

                // Read all bytes from the image file
                byte[] imageData = File.ReadAllBytes(fullPath);

                _imageDataRaw = imageData;
            }
        }

        // Writes a byte array to an image file
        private void WriteImageToFile()
        {
            lock (_lock)
            {
                if (_imageDataRaw is null)
                    throw new InvalidOperationException("Image data is null");

                // Compute the absolute path from the executable location and the relative path provided
                string fullPath = Path.GetFullPath(Path.Combine("../data", _filePath));

                // Ensure the directory exists before writing the file
                string? directoryPath = Path.GetDirectoryName(fullPath);
                if (directoryPath is not null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // Write all bytes to the image file
                File.WriteAllBytes(fullPath, _imageDataRaw);
            }
        }

        private string GenerateImagePath()
        {
            var creationTime = this.DateAdded;
            var sha1Checksum = this.SHA1Hash;
            var type = this.Type;
            var fileExtension = GetImageExtension(this.Image);

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

        public static async Task<FoxImage> Create(ulong user_id, byte[] image, ImageType type, string? filename = null, TgInfo? tgInfo = null)
        {
            var img = new FoxImage();

            img.UserID = user_id;
            img.Type = type;
            img.DateAdded = DateTime.Now;
            img.Image = image;
            
            img._originalFilename = filename ?? $"{img.SHA1Hash}.jpg";

            img._telegramInfo = tgInfo;

            await img.Save();

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

        public async Task Save()
        {
            lock (_lock) {
                if (this.DateAdded == DateTime.MinValue)
                    this.DateAdded = DateTime.Now;

                if (_isDirty)
                    WriteImageToFile();

                PopulateDimensions();
            }

            await FoxDB.SaveObjectAsync(this, "images");

            if (this._telegramInfo is not null)
                await this._telegramInfo.Save(this.ID);

            _isDirty = false;
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

        public static async Task<FoxImage?> Load(ulong image_id)
        {

            var img = await FoxDB.LoadObjectAsync<FoxImage>("images", "id = @id", new Dictionary<string, object?> { { "id", image_id } });

            if (img is not null)
            {
                img._isDirty = false;
            }

            return img;
        }

        private static string GenerateSHA1Hash(byte[] input)
        {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(input));
        }

        public static async Task<FoxImage?> SaveImageFromReply(FoxTelegram t, Message message)
        {
            if (message is null)
                return null; //Nothing we can do.

            TL.Message? newMessage = await t.GetReplyMessage(message);

            if (newMessage is not null && newMessage.media is MessageMediaPhoto { photo: Photo photo })
                return await SaveImageFromTelegram(t, message, photo, true, true);

            return null;
        }

        public static async Task<FoxImage?> CreateFromTgFile(FoxUser user, FoxTelegram t, Message message, Photo photo)
        {
            MemoryStream memoryStream = new MemoryStream();

            var fileType = await FoxTelegram.Client.DownloadFileAsync(photo, memoryStream, photo.LargestPhotoSize);
            var fileName = $"{photo.id}.jpg";

            if (fileType is not Storage_FileType.unknown and not Storage_FileType.partial)
                fileName = $"{photo.id}.{fileType}";

            var tgInfo = new FoxImage.TgInfo(t, message);
            var newImg = await FoxImage.Create(user.UID, memoryStream.ToArray(), FoxImage.ImageType.INPUT, fileName, tgInfo);

            return newImg;
        }

        public static async Task<FoxImage?> SaveImageFromTelegram(FoxTelegram t, Message message, Photo photo, bool Silent = false, bool forceSaveImage = false)
        {
            try
            {
                FoxLog.WriteLine($"Got a photo from {t.User} ({message.ID})!");

                var user = await FoxUser.GetByTelegramUser(t.User, true);

                if (user is null)
                    return null; // User not found, ignore.

                if (user.GetAccessLevel() == AccessLevel.BANNED)
                    return null; // Silently ignore banned users.

                var saveImage = false; // Whether we should save the image or not.

                if (forceSaveImage)
                {
                    saveImage = true; // select command uses this
                }
                if (t.Chat is null) //Only save & notify outside of groups.
                {
                    saveImage = true;
                }
                else if (t.Chat is not null)
                {
                    // We need to check if the user replied to one of our messages or tagged us.

                    if (message.ReplyTo is not null && message.ReplyTo is MessageReplyHeader mrh)
                    {
                        long userId = 0;
                        try
                        {
                            switch (t.Chat)
                            {
                                case Channel channel:
                                    var rmsg = await FoxTelegram.Client.Channels_GetMessages(channel, new InputMessage[] { mrh.reply_to_msg_id });

                                    if (rmsg is not null && rmsg.Messages is not null && rmsg.Messages.First() is not null && rmsg.Messages.First().From is not null)
                                        userId = rmsg.Messages.First().From;
                                    break;
                                case Chat chat:
                                    var crmsg = await FoxTelegram.Client.Messages_GetMessages(new InputMessage[] { mrh.reply_to_msg_id });

                                    if (crmsg is not null && crmsg.Messages is not null && crmsg.Messages.First() is not null && crmsg.Messages.First().From is not null)
                                        userId = crmsg.Messages.First().From;
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex, "Error fetching replied message: " + ex.Message);
                        }

                        if (userId == FoxTelegram.Client.UserId)
                            saveImage = true;
                    }
                    else if (message.entities is not null)
                    {
                        foreach (var entity in message.entities)
                        {
                            if (entity is MessageEntityMention)
                            {
                                var username = message.message.Substring(entity.offset, entity.length);

                                if (username == $"@{FoxTelegram.Client.User.username}")
                                    saveImage = true;
                            }
                        }
                    }
                }

                if (saveImage)
                {
                    var newImg = await CreateFromTgFile(user, t, message, photo);

                    if (newImg is null)
                    {
                        if (!Silent)
                        {
                            await t.SendMessageAsync(
                                text: "❌ Failed to save image.",
                                replyToMessage: message
                            );
                        }
                        return null;
                    }

                    if (!FoxContentFilter.ImageTagsSafetyCheck(await newImg.GetImageTagsAsync()))
                    {
                        newImg.Flagged = true;
                        await newImg.Save();

                        if (!Silent && (t.Chat is null))
                        {
                            await t.SendMessageAsync(
                                text: "⚠️ The image you uploaded was flagged by the content filter and will not be used.\r\n\r\nIf you believe this is a mistake, please contact support via @makefoxhelpbot.",
                                replyToMessage: message
                            );
                        }
                        else
                            throw new Exception("Image was rejected by content filter.");

                        return null; // Image was flagged, do not proceed.
                    }

                    var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

                    settings.SelectedImage = newImg.ID;

                    await settings.Save();

                    if (!Silent)
                    {
                        await t.SendMessageAsync(
                            text: "✅ Image saved and selected as input for /img2img",
                            replyToMessage: message
                        );
                    }

                    return newImg;
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Error with input image: {ex.Message}");
                throw;
            }

            return null;
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
        public async Task<Dictionary<string, float>> GetImageTagsAsync()
        {
            if (_imageTags is not null)
                return _imageTags; // Already loaded

            var tags = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                await conn.OpenAsync();

                const string sql = @"
                    SELECT tag, probability
                    FROM images_tags
                    WHERE id = @id;
                ";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", this.ID);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string tag = reader["tag"].ToString() ?? string.Empty;
                    float prob = Convert.ToSingle(reader["probability"]);

                    tags[tag] = prob;
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, "Error loading image tags: " + ex.Message);
            }

            if (tags.Count < 1)
            {
                // Generate the tags and save them
                FoxONNXImageTagger tagger = new FoxONNXImageTagger();

                _imageTags = tagger.ProcessImage(this.GetRGBAImage(), 0.2f);

                await SaveImageTagsAsync();
            }
            else
            {
                _imageTags = tags;
            }

            return _imageTags ?? new Dictionary<string, float>();
        }

        private async Task SaveImageTagsAsync()
        {
            if (_imageTags is null || _imageTags.Count == 0)
                return; // nothing to save

            try
            {
                using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
                await conn.OpenAsync();

                using var tx = await conn.BeginTransactionAsync();

                // Step 1: delete all existing tags for this image
                using (var deleteCmd = new MySqlCommand(
                    "DELETE FROM images_tags WHERE id = @id;", conn, tx))
                {
                    deleteCmd.Parameters.AddWithValue("@id", this.ID);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // Step 2: insert the new tags
                const string insertSql = @"
                    INSERT INTO images_tags (id, tag, probability)
                    VALUES (@id, @tag, @prob);
                ";

                using var insertCmd = new MySqlCommand(insertSql, conn, tx);
                var pId = insertCmd.Parameters.Add("@id", MySqlDbType.Int64);
                var pTag = insertCmd.Parameters.Add("@tag", MySqlDbType.VarChar);
                var pProb = insertCmd.Parameters.Add("@prob", MySqlDbType.Float);

                foreach (var kv in _imageTags)
                {
                    pId.Value = this.ID;
                    pTag.Value = kv.Key;
                    pProb.Value = kv.Value;

                    await insertCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, "Error saving image tags: " + ex.Message);
            }
        }

    }
}
