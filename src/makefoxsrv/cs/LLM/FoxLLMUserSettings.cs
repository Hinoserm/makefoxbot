using AsyncKeyedLock;
using makefoxsrv.commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    internal class FoxLLMUserSettings
    {
        private static readonly FoxCache<FoxLLMUserSettings> _cache = new FoxCache<FoxLLMUserSettings>(TimeSpan.FromHours(32));
        private static readonly AsyncKeyedLocker<ulong> _cacheLocks = new();

        public static int CacheCount()
        {
            return _cache.Count;
        }

        [DbColumn("user_id")]
        public ulong UserId { private set; get; }

        [DbColumn("selected_persona")]
        public string SelectedPersona = FoxSettings.Get<string?>("DefaultLLMPersona")?.ToUpper() ?? "PROFESSOR";

        [DbColumn("history_start_date")]
        private DateTime? _historyStartDate;

        public PersonalityTraits PersonalityFlags;

        public DateTime HistoryStartDate
        {
            get => _historyStartDate ?? DateTime.MinValue;
            private set => _historyStartDate = value;
        }

        public async Task ClearHistoryAsync(DateTime? toWhen = null)
        {
            _historyStartDate = toWhen ?? DateTime.Now;
            await Save();
        }

        public static async Task<FoxLLMUserSettings> GetSettingsAsync(FoxUser user)
        {
            if (user is null)
                throw new ArgumentNullException(nameof(user));

            var uid = user.UID;

            var cached = _cache.Get(uid);
            if (cached is not null)
                return cached;

            using var _ = await _cacheLocks.LockAsync(uid);

            cached = _cache.Get(uid);
            if (cached is not null)
                return cached;

            var result = await FoxDB.LoadObjectAsync<FoxLLMUserSettings>("llm_user_settings", "user_id = @uid", new Dictionary<string, object?> { { "uid", uid } });

            if (result is null)
            {
                result = new FoxLLMUserSettings
                {
                    UserId = uid,
                    SelectedPersona = FoxSettings.Get<string?>("DefaultLLMPersona") ?? "Professor"
                };

                await result.Save();
            }

            // seed the cache so future Load() calls reuse this object
            _cache.Put(result.UserId, result);

            return result;
        }
        public async Task Save()
        {
            if (UserId == 0)
                throw new InvalidOperationException("Cannot save FoxLLMUserSettings with UserId of 0.");

            await FoxDB.SaveObjectAsync(this, "llm_user_settings");
        }
    }
}
