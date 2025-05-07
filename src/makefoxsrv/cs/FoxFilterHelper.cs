using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace makefoxsrv
{
    static class FoxFilterHelper
    {
        public static void AppendSqlCondition(string column, JsonNode filter, StringBuilder sql, MySqlCommand cmd)
        {
            if (filter is JsonValue scalar)
            {
                var val = NormalizeJsonValue(scalar);
                var param = $"@{column}_{cmd.Parameters.Count}";
                sql.Append($" AND {column} = {param}");
                cmd.Parameters.AddWithValue(param, val ?? DBNull.Value);
                return;
            }

            if (filter is JsonArray arr)
            {
                var includes = new List<string>();
                var excludes = new List<string>();
                foreach (var elem in arr)
                {
                    if (elem is JsonObject obj && obj.TryGetPropertyValue("not", out var notNode))
                    {
                        var val = NormalizeJsonValue(notNode);
                        var param = $"@{column}_{cmd.Parameters.Count}";
                        excludes.Add(param);
                        cmd.Parameters.AddWithValue(param, val ?? DBNull.Value);
                    }
                    else if (elem is JsonValue vNode)
                    {
                        var val = NormalizeJsonValue(vNode);
                        var param = $"@{column}_{cmd.Parameters.Count}";
                        includes.Add(param);
                        cmd.Parameters.AddWithValue(param, val ?? DBNull.Value);
                    }
                }
                if (includes.Count > 0)
                    sql.Append($" AND {column} IN ({string.Join(", ", includes)})");
                foreach (var ex in excludes)
                    sql.Append($" AND {column} <> {ex}");
                return;
            }

            if (filter is JsonObject ops)
            {
                foreach (var kv in ops)
                {
                    var op = kv.Key;
                    var val = NormalizeJsonValue(kv.Value!);
                    var param = $"@{column}_{cmd.Parameters.Count}";
                    switch (op)
                    {
                        case "lt": sql.Append($" AND {column} < {param}"); break;
                        case "lte": sql.Append($" AND {column} <= {param}"); break;
                        case "gt": sql.Append($" AND {column} > {param}"); break;
                        case "gte": sql.Append($" AND {column} >= {param}"); break;
                        case "not": sql.Append($" AND {column} <> {param}"); break;
                        default: continue;
                    }
                    cmd.Parameters.AddWithValue(param, val ?? DBNull.Value);
                }
            }
        }

        public static bool Matches(JsonNode filter, object? actual, string? key = null)
        {
            if (filter is JsonValue scalar)
            {
                var expected = scalar.GetValue<object>();

                // Handle 'contains' type match if key ends with _contains
                if (key is not null && key.EndsWith("contains", StringComparison.OrdinalIgnoreCase))
                {
                    string a = actual?.ToString() ?? "";
                    string e = expected?.ToString() ?? "";
                    return a.Contains(e, StringComparison.OrdinalIgnoreCase);
                }

                return ValuesEqual(actual, expected);
            }

            if (filter is JsonArray arr)
            {
                var includes = new List<object?>();
                var excludes = new List<object?>();

                foreach (var elem in arr)
                {
                    if (elem is JsonObject obj && obj.TryGetPropertyValue("not", out var notNode))
                        excludes.Add(((JsonValue)notNode!).GetValue<object>());
                    else if (elem is JsonValue vNode)
                        includes.Add(vNode.GetValue<object>());
                }

                if (excludes.Any(e => ValuesEqual(actual, e)))
                    return false;

                if (includes.Count > 0)
                    return includes.Any(i => ValuesEqual(actual, i));

                return true;
            }

            if (filter is JsonObject ops)
            {
                foreach (var kv in ops)
                {
                    var op = kv.Key;
                    var v = ((JsonValue)kv.Value!).GetValue<object>();

                    switch (op)
                    {
                        case "not":
                            if (ValuesEqual(actual, v))
                                return false;
                            break;
                        case "lt":
                            if (!Compare(actual, v, (l, r) => l < r))
                                return false;
                            break;
                        case "lte":
                            if (!Compare(actual, v, (l, r) => l <= r))
                                return false;
                            break;
                        case "gt":
                            if (!Compare(actual, v, (l, r) => l > r))
                                return false;
                            break;
                        case "gte":
                            if (!Compare(actual, v, (l, r) => l >= r))
                                return false;
                            break;
                    }
                }

                return true;
            }

            return true;
        }

        static bool Compare(object? left, object? right, Func<double, double, bool> cmp)
        {
            if (left == null || right == null)
                return false;

            try
            {
                var dL = Convert.ToDouble(left);
                var dR = Convert.ToDouble(right);

                return cmp(dL, dR);
            }
            catch
            {
                return false;
            }
        }

        static bool ValuesEqual(object? a, object? b)
        {
            return string.Equals(a?.ToString(), b?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public static object? NormalizeJsonValue(JsonNode? node)
        {
            if (node is null) return null;
            if (node is JsonValue value)
            {
                if (value.TryGetValue(out bool b))
                    return b;

                if (value.TryGetValue(out int i))
                    return i;

                if (value.TryGetValue(out long l))
                    return l;

                if (value.TryGetValue(out double d))
                    return d;

                if (value.TryGetValue(out string? s))
                    return s;
            }
            return node.ToString();
        }
    }
}
