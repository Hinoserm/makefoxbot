using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace makefoxsrv
{
    internal class FoxJsonHelper
    {
        // Get a string parameter
        public static string? GetString(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Return the parameter as a string
            return paramNode.ToString();
        }

        // Get an int parameter
        public static int? GetInt(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Try to convert the parameter to int
            if (!int.TryParse(paramNode.ToString(), out int result))
                throw new Exception($"Parameter '{paramName}' must be a valid integer but was '{paramNode.ToString()}'.");

            return result;
        }

        // Get a long parameter
        public static long? GetLong(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Try to convert the parameter to long
            if (!long.TryParse(paramNode.ToString(), out long result))
                throw new Exception($"Parameter '{paramName}' must be a valid long integer but was '{paramNode.ToString()}'.");

            return result;
        }

        // Get a boolean parameter
        public static bool? GetBool(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Try to convert the parameter to boolean
            if (!bool.TryParse(paramNode.ToString(), out bool result))
                throw new Exception($"Parameter '{paramName}' must be a valid boolean but was '{paramNode.ToString()}'.");

            return result;
        }

        // Get a double parameter
        public static double? GetDouble(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Try to convert the parameter to double
            if (!double.TryParse(paramNode.ToString(), out double result))
                throw new Exception($"Parameter '{paramName}' must be a valid double but was '{paramNode.ToString()}'.");

            return result;
        }

        // Get a list of string parameters
        public static List<string>? GetStringList(JsonObject jsonMessage, string paramName, bool optional = true)
        {
            if (!jsonMessage.TryGetPropertyValue(paramName, out JsonNode? paramNode) || paramNode == null)
            {
                if (optional)
                    return null;
                else
                    throw new Exception($"Parameter '{paramName}' is required but not provided.");
            }

            // Ensure the parameter is an array
            if (paramNode is not JsonArray jsonArray)
                throw new Exception($"Parameter '{paramName}' must be an array.");

            // Convert the JsonArray to a list of strings
            var resultList = jsonArray.Select(node => node?.ToString() ?? string.Empty).ToList();

            return resultList;
        }

    }
}
