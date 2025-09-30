#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BotCallableAttribute : Attribute
    {
        public bool AdminOnly { get; }

        public BotCallableAttribute(bool adminOnly = false)
        {
            AdminOnly = adminOnly;
        }
    }

    public static class FoxCallbackHandler
    {
        private static readonly Dictionary<int, (MethodInfo Method, bool AdminOnly)> _registry = new();
        private static readonly Dictionary<MethodInfo, int> _reverse = new();
        private static int _nextId = 1;

        static FoxCallbackHandler()
        {
            var asm = Assembly.GetExecutingAssembly();

            foreach (var type in asm.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<BotCallableAttribute>();

                    if (attr != null)
                    {
                        var id = _nextId++;
                        _registry[id] = (method, attr.AdminOnly);
                        _reverse[method] = id;
                    }
                }
            }
        }

        public static string BuildCallbackData(MethodInfo method, params object?[] args)
        {
            if (!_reverse.TryGetValue(method, out var funcId))
                throw new InvalidOperationException("Method not registered");

            var tokens = new List<string>();

            foreach (var arg in args)
            {
                if (arg == null)
                {
                    tokens.Add("!");
                }
                else if (arg is string s)
                {
                    tokens.Add(EscapeString(s));
                }
                else if (arg is System.Collections.IEnumerable enumerable && arg is not string)
                {
                    var list = enumerable.Cast<object?>().ToList();

                    tokens.Add(list.Count.ToString());

                    foreach (var item in list)
                    {
                        if (item == null)
                        {
                            tokens.Add("!");
                        }
                        else if (item is bool bItem)
                        {
                            tokens.Add(bItem ? "1" : "0");
                        }
                        else if (item is string sItem)
                        {
                            tokens.Add(EscapeString(sItem));
                        }
                        else if (item.GetType().IsEnum)
                        {
                            tokens.Add(Convert.ToUInt64(item).ToString());
                        }
                        else
                        {
                            tokens.Add(item.ToString() ?? "");
                        }
                    }
                }
                else if (arg is bool b)
                {
                    tokens.Add(b ? "1" : "0");
                }
                else if (arg.GetType().IsEnum)
                {
                    tokens.Add(Convert.ToUInt64(arg).ToString());
                }
                else
                {
                    tokens.Add(arg.ToString() ?? "");
                }
            }

            return "/x " + funcId + ":" + string.Join(",", tokens);
        }

        public static string BuildCallbackData(Delegate del, params object?[] args)
        {
            return BuildCallbackData(del.Method, args);
        }

        private static object? ConvertToken(string tok, Type targetType)
        {
            if (tok == "!")
            {
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    return null;

                if (!targetType.IsValueType) // reference types (like string)
                    return null;

                throw new InvalidOperationException(
                    $"Null token not allowed for non-nullable {targetType.Name}");
            }

            if (targetType == typeof(string))
                return UnescapeString(tok);

            if (targetType == typeof(bool))
                return tok == "1" || tok.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (targetType.IsEnum)
                return Enum.ToObject(targetType, ulong.Parse(tok));

            if (targetType == typeof(int))
                return int.Parse(tok);

            if (targetType == typeof(uint))
                return uint.Parse(tok);

            if (targetType == typeof(long))
                return long.Parse(tok);

            if (targetType == typeof(ulong))
                return ulong.Parse(tok);

            throw new NotSupportedException(
                $"Unsupported parameter type: {targetType.Name}");
        }

        private static string EscapeString(string s)
        {
            return s
                .Replace("%", "%25")
                .Replace(",", "%2C")
                .Replace(":", "%3A")
                .Replace("!", "%21");
        }

        private static string UnescapeString(string s)
        {
            return s
                .Replace("%21", "!")
                .Replace("%3A", ":")
                .Replace("%2C", ",")
                .Replace("%25", "%");
        }

        public static async Task Dispatch(
            string callbackData,
            FoxTelegram telegram,
            FoxUser user,
            UpdateBotCallbackQuery query)
        {
            try
            {
                var parts = callbackData.Split(':', 2);

                if (parts.Length < 1)
                    throw new InvalidOperationException("Bad callbackData");

                var funcId = int.Parse(parts[0]);

                if (!_registry.TryGetValue(funcId, out var entry))
                    throw new InvalidOperationException("Unknown function");

                var isAdmin = user.GetAccessLevel() >= AccessLevel.ADMIN;

                if (entry.AdminOnly && !isAdmin)
                    throw new UnauthorizedAccessException("This action requires admin privileges.");

                var method = entry.Method;
                var paramInfos = method.GetParameters();
                var args = new List<object?>();

                // Must start with FoxTelegram, FoxUser, UpdateBotCallbackQuery
                if (paramInfos.Length < 3 ||
                    paramInfos[0].ParameterType != typeof(FoxTelegram) ||
                    paramInfos[1].ParameterType != typeof(FoxUser) ||
                    paramInfos[2].ParameterType != typeof(UpdateBotCallbackQuery))
                {
                    throw new InvalidOperationException(
                        $"BotCallable method {method.Name} must have (FoxTelegram, FoxUser, UpdateBotCallbackQuery) as its first 3 parameters.");
                }

                // Add those 3 first
                args.Add(telegram);
                args.Add(user);
                args.Add(query);

                // Any tokens after funcId
                var argTokens = new Queue<string>();

                if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
                {
                    foreach (var tok in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        argTokens.Enqueue(tok);
                }

                // Parse remaining method params (after the first 3)
                for (int i = 3; i < paramInfos.Length; i++)
                {
                    var p = paramInfos[i];
                    var pType = p.ParameterType;

                    if (pType.IsGenericType && pType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        if (argTokens.Count == 0)
                            throw new InvalidOperationException("Missing list length");

                        var lenToken = argTokens.Dequeue();

                        if (lenToken == "!")
                        {
                            args.Add(null);
                            continue;
                        }

                        int len = int.Parse(lenToken);

                        var list = (System.Collections.IList)Activator.CreateInstance(pType)!;
                        var elemType = pType.GetGenericArguments()[0];

                        for (int j = 0; j < len; j++)
                        {
                            if (argTokens.Count == 0)
                                throw new InvalidOperationException("Missing list element");

                            var tok = argTokens.Dequeue();

                            if (tok == "!")
                            {
                                list.Add(null);
                            }
                            else
                            {
                                list.Add(ConvertToken(tok, elemType));
                            }
                        }

                        args.Add(list);
                    }
                    else if (pType.IsArray)
                    {
                        if (argTokens.Count == 0)
                            throw new InvalidOperationException("Missing array length");

                        var lenToken = argTokens.Dequeue();

                        if (lenToken == "!")
                        {
                            args.Add(null);
                            continue;
                        }

                        int len = int.Parse(lenToken);

                        var elemType = pType.GetElementType()!;
                        var array = Array.CreateInstance(elemType, len);

                        for (int j = 0; j < len; j++)
                        {
                            if (argTokens.Count == 0)
                                throw new InvalidOperationException("Missing array element");

                            var tok = argTokens.Dequeue();

                            if (tok == "!")
                            {
                                array.SetValue(null, j);
                            }
                            else
                            {
                                array.SetValue(ConvertToken(tok, elemType), j);
                            }
                        }

                        args.Add(array);
                    }
                    else
                    {
                        if (argTokens.Count == 0)
                            throw new InvalidOperationException("Missing argument");

                        var tok = argTokens.Dequeue();

                        if (tok == "!" && pType.IsGenericType && pType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            args.Add(null);
                        }
                        else
                        {
                            args.Add(ConvertToken(tok, Nullable.GetUnderlyingType(pType) ?? pType));
                        }
                    }
                }

                var result = method.Invoke(null, args.ToArray());

                if (result is Task t)
                {
                    await t.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);

                await telegram.SendCallbackAnswer(query.query_id, 0, $"❌ Error: {ex.Message}", null, true);
            }
        }

        private static bool IsListType(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static bool IsNullable(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
        }
    }
}
