using AsyncKeyedLock;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv.telegram
{
    internal class FoxTelegramBot : IAsyncDisposable
    {
        [DbColumn("id")]
        public int Id { get; private set; }

        [DbColumn("bot_token")]
        public string BotToken { get; private set; } = "";

        [DbColumn("bot_name")]
        public string BotName { get; private set; } = "";

        [DbColumn("bot_peer_id")]
        public long PeerId { get; private set; }

        [DbColumn("owner_peer_id")]
        public long OwnerPeerId { get; private set; }

        [DbColumn("enabled")]
        public bool Enabled { get; private set; }

        [DbColumn("date_added")]
        public DateTime DateAdded { get; private set; }

        [DbColumn("date_last_seen")]
        public DateTime? DateLastSeen { get; private set; }

        //Not yet implemented
        //public FoxTelegramPeer Owner { get; private set; }

        private TL.User? _tgUser;

        private WTelegram.Client? _client;

        private static readonly FoxCache<FoxTelegramBot> _cache = new FoxCache<FoxTelegramBot>(TimeSpan.FromHours(72));
        private static readonly AsyncKeyedLocker<ulong> _cacheLocks = new();

        // Create WTelegram.Client and connect to Telegram
        public async Task ConnectAsync()
        {
            if (_client is not null)
                throw new InvalidOperationException("Bot is already connected.");

            if (FoxMain.settings?.TelegramApiId is null)
                throw new InvalidOperationException("Telegram API ID is not configured.");

            if (string.IsNullOrEmpty(FoxMain.settings?.TelegramApiHash))
                throw new InvalidOperationException("Telegram API Hash is not configured.");

            var sessionFile = $"../conf/telegram-{PeerId}.session";

            var telegramApiId = FoxMain.settings.TelegramApiId.Value;
            var telegramApiHash = FoxMain.settings.TelegramApiHash;

            var tgClient = new WTelegram.Client(telegramApiId, telegramApiHash, sessionFile);
            //_client.OnOther += Client_OnOther;
            tgClient.OnUpdates += this.HandleUpdateAsync;
            //_client.OnOwnUpdates += HandleUpdateAsync;


            tgClient.MaxAutoReconnects = 100000;
            tgClient.FloodRetryThreshold = 0;

            await tgClient.LoginBotIfNeeded(BotToken);

            _tgUser = tgClient.User;

            if (_tgUser.ID != PeerId)
            {
                await tgClient.DisposeAsync();
                throw new InvalidOperationException($"Bot token is for bot {_tgUser.username} (id {_tgUser.ID}), but expected id {PeerId}.");
            }

            _client = tgClient;

            FoxLog.WriteLine($"BOT {Id} is logged-in as {_client.User} (id {_client.User.ID})");

            await FoxCommandHandler.SetBotCommands(_client);
        }

        private async Task HandleUpdateAsync(UpdatesBase updates)
        {
            // Do Important Stuff Here
        }

        public static async Task<FoxTelegramBot?> Load(ulong id)
        {
            var cached = _cache.Get(id);
            if (cached is not null)
                return cached;

            using var _ = await _cacheLocks.LockAsync(id);

            cached = _cache.Get(id);
            if (cached is not null)
                return cached;

            var bot = await FoxDB.LoadObjectAsync<FoxTelegramBot>("telegram_bots", "id = @id", new Dictionary<string, object?> { { "id", id } });

            if (bot is not null)
                _cache.Put(id, bot);

            return bot;
        }

        // Used pretty much only in the handler that starts all the bots
        public static async Task<List<FoxTelegramBot>> LoadAllEnabled()
        {
            var bots = await FoxDB.LoadMultipleAsync<FoxTelegramBot>("telegram_bots", "enabled = 1");
            foreach (var bot in bots)
            {
                _cache.Put((ulong)bot.Id, bot);
            }
            return bots;
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }
    }
}
