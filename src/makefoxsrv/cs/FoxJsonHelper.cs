using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace makefoxsrv
{
    internal class FoxJsonHelper
    {
        // Case-insensitive property lookup
        private static JsonNode? GetPropertyIgnoreCase(JsonObject jsonMessage, string paramName)
        {
            foreach (var kvp in jsonMessage)
            {
                if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        public static string? GetString(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            return paramNode.ToString();
        }

        public static int? GetInt(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            if (!int.TryParse(paramNode.ToString(), out int result))
                throw new Exception($"Parameter '{paramName}' must be a valid integer but was '{paramNode}'.");

            return result;
        }

        public static long? GetLong(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            if (!long.TryParse(paramNode.ToString(), out long result))
                throw new Exception($"Parameter '{paramName}' must be a valid long integer but was '{paramNode}'.");

            return result;
        }

        public static bool? GetBool(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            if (!bool.TryParse(paramNode.ToString(), out bool result))
                throw new Exception($"Parameter '{paramName}' must be a valid boolean but was '{paramNode}'.");

            return result;
        }

        public static double? GetDouble(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            if (!double.TryParse(paramNode.ToString(), out double result))
                throw new Exception($"Parameter '{paramName}' must be a valid double but was '{paramNode}'.");

            return result;
        }

        public static List<string>? GetStringList(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            var paramNode = GetPropertyIgnoreCase(jsonMessage, paramName);
            if (paramNode == null)
            {
                if (optional)
                    return null;
                throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            if (paramNode is not JsonArray jsonArray)
                throw new Exception($"Parameter '{paramName}' must be an array.");

            return jsonArray.Select(n => n?.ToString() ?? string.Empty).ToList();
        }
    }
}
