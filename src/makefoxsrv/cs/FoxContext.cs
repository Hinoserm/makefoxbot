using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Threading;
using WTelegram;
using TL;
using System.Collections.Concurrent;

namespace makefoxsrv
{
    internal class FoxContext
    {
        // Context properties
        public FoxQueue? Queue { get; set; } = null;
        public FoxUser? User { get; set; } = null;
        public FoxTelegram? Telegram { get; set; } = null;
        public FoxWorker? Worker { get; set; } = null;
        public dynamic? Message { get; set; } = null;
        public string? Command { get; set; } = null;
        public string? Argument { get; set; } = null;
    }

    internal static class FoxContextManager
    {
        private static readonly AsyncLocal<FoxContext?> _context = new();

        public static FoxContext Current
        {
            get => _context.Value ?? throw new InvalidOperationException("No context is set.");
            set => _context.Value = value ?? throw new ArgumentNullException(nameof(value), "Context cannot be null.");
        }

        public static void Clear() => _context.Value = null;
    }


    /*
    internal static class FoxContextManager
    {
        private static readonly ConcurrentDictionary<int, FoxContext> _contexts = new();
        private static readonly AsyncLocal<FoxContext?> _defaultContext = new();

        public static FoxContext Current
        {
            get
            {
                int key = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                if (!_contexts.TryGetValue(key, out var context))
                {
                    if (_defaultContext.Value != null)
                        return _defaultContext.Value;
                    throw new InvalidOperationException("No context is set for the current execution.");
                }
                return context;
            }
            set
            {
                int key = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                if (Task.CurrentId == null)
                {
                    _defaultContext.Value = value; // Use AsyncLocal for thread-based fallback
                }
                else
                {
                    _contexts[key] = value;
                }
            }
        }

        public static void Clear()
        {
            int key = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
            if (Task.CurrentId == null)
            {
                _defaultContext.Value = null;
            }
            else
            {
                _contexts.TryRemove(key, out _);
            }
        }
    } */

}
