using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv
{
    public static class FoxLLMBatchHandler
    {
        private sealed class UserState
        {
            public readonly List<TL.Message> Messages = new();
            public FoxTelegram Telegram;
            public FoxUser User;
            public CancellationTokenSource? TimerCts;
            public CancellationTokenSource? TypingCts;
            public Task? TypingTask;
            public bool IsProcessing;
            public DateTime LastMessageTime;

            public UserState(FoxTelegram telegram, FoxUser user)
            {
                Telegram = telegram;
                User = user;
            }
        }

        private static readonly ConcurrentDictionary<ulong, UserState> _states = new();

        private const double BaseDelaySeconds = 8.0;
        private const double IncrementSeconds = 0.6;
        private const double MaxDelaySeconds = 15.0;

        public static void StartCleanupLoop(TimeSpan interval, TimeSpan idleTimeout, CancellationToken token)
            => _ = Task.Run(() => CleanupLoopAsync(interval, idleTimeout, token), token);

        public static async Task AddMessageAsync(FoxTelegram telegram, FoxUser user, Message message, CancellationToken token)
        {
            await FoxLLMConversation.InsertConversationMessageAsync(user, "user", message.message, message);

            var state = _states.GetOrAdd(user.UID, _ => new UserState(telegram, user));

            lock (state)
            {
                state.Telegram = telegram;
                state.User = user;
                state.Messages.Add(message);
                state.LastMessageTime = DateTime.UtcNow;

                // If LLM is processing, queue new messages but do not trigger another flush until done.
                if (state.IsProcessing)
                    return;

                // Reset timer on new message
                state.TimerCts?.Cancel();
                state.TimerCts = new CancellationTokenSource();

                // Start typing if not already
                if (state.TypingTask is null)
                {
                    state.TypingCts = new CancellationTokenSource();
                    state.TypingTask = SimulateTyping(state, state.TypingCts.Token);
                }

                _ = RunTimerAsync(state, token);
            }
        }

        private static async Task RunTimerAsync(UserState s, CancellationToken globalToken)
        {
            // Timer should only trigger once inactivity period passes without new input
            while (true)
            {
                CancellationTokenSource? localCts;
                double delay;

                lock (s)
                {
                    if (s.Messages.Count == 0 || s.IsProcessing)
                        return;

                    delay = Math.Min(BaseDelaySeconds + (s.Messages.Count - 1) * IncrementSeconds, MaxDelaySeconds);
                    localCts = s.TimerCts;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), localCts!.Token);
                    break; // timer expired normally
                }
                catch (TaskCanceledException)
                {
                    // new message arrived, restart the delay
                    continue;
                }
            }

            await FlushAsync(s, globalToken);
        }

        private static async Task SimulateTyping(UserState s, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await FoxTelegram.Client.Messages_SetTyping(s.Telegram.Peer, new SendMessageTypingAction());
                    await Task.Delay(1000, token);
                }
            }
            catch (TaskCanceledException)
            {
                // expected
            }
            finally
            {
                await FoxTelegram.Client.Messages_SetTyping(s.Telegram.Peer, new SendMessageCancelAction());
                lock (s)
                {
                    s.TypingTask = null;
                    s.TypingCts = null;
                }
            }
        }

        private static async Task FlushAsync(UserState s, CancellationToken token)
        {
            List<Message> toProcess;

            lock (s)
            {
                if (s.Messages.Count == 0 || s.IsProcessing)
                    return;

                s.IsProcessing = true;
                toProcess = new List<Message>(s.Messages);
                s.Messages.Clear();

                s.TimerCts?.Cancel();
                s.TimerCts = null;
            }

            try
            {
                // Keep typing active while processing
                await FoxLLM.ProcessLLMRequest(s.Telegram, s.User);
            }
            finally
            {
                s.TypingCts?.Cancel();

                // Reset state after completion so batching resumes correctly
                lock (s)
                {
                    s.IsProcessing = false;
                }

                // If user typed during LLM call, start new timer immediately
                if (s.Messages.Count > 0)
                {
                    s.TimerCts = new CancellationTokenSource();
                    _ = RunTimerAsync(s, token);
                    if (s.TypingTask is null)
                    {
                        s.TypingCts = new CancellationTokenSource();
                        s.TypingTask = SimulateTyping(s, s.TypingCts.Token);
                    }
                }
            }
        }

        public static async Task FlushAllAsync(CancellationToken token)
        {
            foreach (var (_, s) in _states)
            {
                try { await FlushAsync(s, token); }
                catch { /* ignore */ }
            }
        }

        private static void Cleanup(TimeSpan idleTimeout)
        {
            var now = DateTime.UtcNow;
            foreach (var (uid, s) in _states)
            {
                lock (s)
                {
                    if (!s.IsProcessing && s.Messages.Count == 0 && (now - s.LastMessageTime) > idleTimeout)
                        _states.TryRemove(uid, out _);
                }
            }
        }

        private static async Task CleanupLoopAsync(TimeSpan interval, TimeSpan idleTimeout, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token);
                    Cleanup(idleTimeout);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
