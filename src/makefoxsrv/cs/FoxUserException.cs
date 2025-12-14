using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxUserException : Exception
    {
        public object? Details { get; }

        public FoxUserException(string message, object? details = null)
            : base(message)
        {
            Details = details;
        }
    }
}
