using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Threading;
using WTelegram;
using TL;

namespace makefoxsrv
{
    internal class FoxContext
    {
        // Context properties
        public FoxQueue? Queue { get; set; } = null;
        public FoxUser? User { get; set; } = null;
        public FoxTelegram? Telegram { get; set; } = null;
        public FoxWorker? Worker { get; set; } = null;
        public Message? Message { get; set; } = null;
        public string? Command { get; set; } = null;
        public string? Argument { get; set; } = null;
    }

    internal static class FoxContextManager
    {
        // AsyncLocal storage for the current FoxContext
        private static readonly AsyncLocal<FoxContext> _context = new AsyncLocal<FoxContext>();

        public static FoxContext Current
        {
            get => _context.Value ??= new FoxContext();
            set => _context.Value = value;
        }

        // Clear the current context
        public static void Clear() => _context.Value = null;
    }
}
