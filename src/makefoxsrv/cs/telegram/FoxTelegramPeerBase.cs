//using System;
//using System.Threading.Tasks;
//using TL;
//using MySqlConnector;

//namespace makefoxsrv.telegram
//{
//    internal abstract class FoxTelegramPeerBase
//    {
//        protected FoxTelegramBot Bot { get; private set; }

//        [DbColumn("bot_id")]
//        public ulong BotId { get; protected set; }

//        [DbColumn("peer_id")]
//        public long PeerId { get; protected set; }

//        [DbColumn("access_hash")]
//        public long? AccessHash { get; protected set; }

//        [DbColumn("peer_type")]
//        public string PeerType { get; protected set; } = "UNKNOWN";

//        protected FoxTelegramPeerBase(FoxTelegramBot bot, long peerId, long? accessHash = null)
//        {
//            Bot = bot;
//            BotId = (ulong)bot.Id;
//            PeerId = peerId;
//            AccessHash = accessHash;
//        }

//        protected FoxTelegramPeerBase()
//        {
//            // Required for FoxDB.LoadObjectAsync()
//            Bot = null!;
//        }

//        public abstract InputPeer GetInputPeer();

//        protected abstract Task SavePeerDetailsAsync();

//        public async Task SaveAsync()
//        {
//            await FoxDB.SaveObjectAsync(this, "telegram_access_hashes");
//            await SavePeerDetailsAsync();
//        }

//        public static async Task<(long? accessHash, string? peerType)> LoadAccessHashAsync(FoxTelegramBot bot, long peerId)
//        {
//            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
//            await SQL.OpenAsync();

//            const string query = @"
//                SELECT access_hash, peer_type
//                FROM telegram_access_hashes
//                WHERE bot_id = @bot_id AND peer_id = @peer_id
//                LIMIT 1;
//            ";

//            using var cmd = new MySqlCommand(query, SQL);
//            cmd.Parameters.AddWithValue("@bot_id", bot.Id);
//            cmd.Parameters.AddWithValue("@peer_id", peerId);

//            using var reader = await cmd.ExecuteReaderAsync();

//            if (!await reader.ReadAsync())
//                return (null, null);

//            long? hash = reader["access_hash"] is DBNull ? null : (long?)reader["access_hash"];
//            string? type = reader["peer_type"] as string;

//            return (hash, type);
//        }

//        protected static async Task SaveAccessHashAsync(FoxTelegramBot bot, long peerId, long? accessHash, string peerType)
//        {
//            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
//            await SQL.OpenAsync();

//            const string query = @"
//                INSERT INTO telegram_access_hashes (bot_id, peer_id, access_hash, peer_type)
//                VALUES (@bot_id, @peer_id, @access_hash, @peer_type)
//                ON DUPLICATE KEY UPDATE
//                    access_hash = VALUES(access_hash),
//                    peer_type = VALUES(peer_type);
//            ";

//            using var cmd = new MySqlCommand(query, SQL);
//            cmd.Parameters.AddWithValue("@bot_id", bot.Id);
//            cmd.Parameters.AddWithValue("@peer_id", peerId);
//            cmd.Parameters.AddWithValue("@access_hash", accessHash ?? (object)DBNull.Value);
//            cmd.Parameters.AddWithValue("@peer_type", peerType);

//            await cmd.ExecuteNonQueryAsync();
//        }

//        public void UpdateAccessHash(long? newHash)
//        {
//            if (newHash == AccessHash)
//                return;

//            AccessHash = newHash;
//        }

//        public virtual void UpdateUsername(string? newUsername)
//        {
//            // Overridden in subclasses (users/chats)
//        }
//    }
//}
