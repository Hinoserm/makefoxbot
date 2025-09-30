#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class BotCommandAttribute : Attribute
    {
        public string Cmd { get; }
        public string? Sub { get; }
        public bool AdminOnly { get; }

        public BotCommandAttribute(string cmd, string? sub = null, bool adminOnly = false)
        {
            Cmd = cmd;
            Sub = sub;
            AdminOnly = adminOnly;
        }
    }

    internal sealed class CommandEntry
    {
        public MethodInfo Method { get; }
        public bool AdminOnly { get; }

        public CommandEntry(MethodInfo method, bool adminOnly)
        {
            Method = method;
            AdminOnly = adminOnly;
        }
    }

    public static class FoxCommandHandler
    {
        private static readonly Dictionary<string, Dictionary<string, CommandEntry>> _commands;

        static FoxCommandHandler()
        {
            _commands = new Dictionary<string, Dictionary<string, CommandEntry>>(StringComparer.OrdinalIgnoreCase);

            var methods = typeof(FoxCommandHandler).Assembly
                .GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m.GetCustomAttributes<BotCommandAttribute>().Any());

            foreach (var method in methods)
            {
                foreach (var attr in method.GetCustomAttributes<BotCommandAttribute>())
                {
                    if (!_commands.TryGetValue(attr.Cmd, out var subs))
                    {
                        subs = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);
                        _commands[attr.Cmd] = subs;
                    }

                    var key = attr.Sub ?? string.Empty;
                    if (subs.ContainsKey(key))
                        throw new InvalidOperationException($"Duplicate command {attr.Cmd} {attr.Sub}");

                    subs[key] = new CommandEntry(method, attr.AdminOnly);
                }
            }
        }

        public static async Task<bool> Dispatch(FoxTelegram t, TL.Message message, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("/"))
                return false;

            text = text[1..];
            var firstSpace = text.IndexOf(' ');
            string command;
            string rest;
            if (firstSpace == -1)
            {
                command = text;
                rest = string.Empty;
            }
            else
            {
                command = text[..firstSpace];
                rest = text[(firstSpace + 1)..].TrimStart();
            }

            var c = command.Split('@', 2);
            if (c.Length == 2)
            {
                var target = c[1].ToLowerInvariant();
                var self = FoxTelegram.Client.User.username.ToLowerInvariant();
                if (target != self)
                    return false;

                command = c[0];
            }

            string? subInput = null;
            string? args = null;
            if (!string.IsNullOrEmpty(rest))
            {
                var secondSpace = rest.IndexOf(' ');
                if (secondSpace == -1)
                    subInput = rest;
                else
                {
                    subInput = rest[..secondSpace];
                    args = rest[(secondSpace + 1)..];
                }
            }

            if (subInput is not null && subInput.StartsWith("#"))
                subInput = subInput[1..];

            var matches = _commands.Keys
                .Where(o => o.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return false;
            if (matches.Count > 1)
            {
                var list = string.Join(" | ", matches.OrderBy(m => m));
                await t.SendMessageAsync(
                    text: $"❌ Ambiguous command: /{command}\n\nDid you mean one of: {list}?",
                    replyToMessage: message);
                return true;
            }

            var cmd = matches[0];
            var subs = _commands[cmd];
            CommandEntry? entry = null;

            if (subInput is null)
            {
                if (subs.TryGetValue(string.Empty, out var e))
                {
                    entry = e;
                    args = rest;
                }
                else
                {
                    var usage = BuildUsage(cmd, subs);
                    await t.SendMessageAsync(text: usage, replyToMessage: message);
                    return true;
                }
            }
            else
            {
                if (subs.ContainsKey(string.Empty) && !subs.Keys.Any(k => k.Length > 0))
                {
                    entry = subs[string.Empty];
                    args = rest;
                }
                else if (subs.TryGetValue(subInput, out var e))
                {
                    cmd = $"{cmd} {subInput}";
                    entry = e;
                }
                else
                {
                    var usage = BuildUsage(cmd, subs);
                    await t.SendMessageAsync(
                        text: $"❌ Unknown subcommand: {subInput}\n\n{usage}",
                        replyToMessage: message);
                    return true;
                }
            }

            try
            {
                if (entry is null)
                    throw new Exception($"No matching handler for /{cmd} {subInput}");

                var user = await FoxUser.GetByTelegramUser(t.User, true);
                if (user is null)
                    throw new Exception("Unable to locate or create new user.");
                if (user.GetAccessLevel() == AccessLevel.BANNED)
                    throw new Exception("You are banned from using this bot.");

                bool isAdmin = user.CheckAccessLevel(AccessLevel.ADMIN);
                if (entry.AdminOnly && !isAdmin)
                    throw new Exception("This command is restricted to administrators.");
                if (!isAdmin && t.Chat is not null &&
                    !await FoxGroupAdmin.CheckGroupTopicAllowed(t.Chat, t.User, message.ReplyHeader?.TopicID ?? 0))
                    throw new Exception("Commands are not permitted in this topic.");

                var invokeArgs = await BuildArguments(entry.Method, t, user, message, args, cmd, subInput);
                var task = (Task?)entry.Method.Invoke(null, invokeArgs);
                if (task is null)
                    throw new Exception($"Command handler {entry.Method.Name} did not return a Task.");
                await task;
            }
            catch (Exception ex)
            {
                await t.SendMessageAsync(
                    text: $"❌ Error: {ex.Message}",
                    replyToMessage: message);
            }

            return true;
        }

        private static async Task<object?[]> BuildArguments(
            MethodInfo method,
            FoxTelegram t,
            FoxUser user,
            TL.Message message,
            string? args,
            string cmd,
            string? subInput)
        {
            var parameters = method.GetParameters();
            var invokeArgs = new List<object?> { t, user, message };
            string remaining = args ?? string.Empty;

            for (int pi = 3; pi < parameters.Length; pi++)
            {
                var param = parameters[pi];
                string paramName = param.Name ?? $"arg{pi}";
                var paramType = param.ParameterType;
                var underlying = Nullable.GetUnderlyingType(paramType);
                bool isNullable = IsNullable(param);
                var targetType = underlying ?? paramType;

                var usageStr = BuildUsageError(method, cmd, subInput);

                if (pi == parameters.Length - 1)
                {
                    // last parameter gets the entire remainder
                    if (targetType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace(remaining) && !isNullable)
                            throw new Exception($"Missing required argument(s).\r\n\r\n{usageStr}");
                        invokeArgs.Add(string.IsNullOrWhiteSpace(remaining) ? null : remaining);
                    }
                    else if (targetType == typeof(FoxUser))
                    {
                        if (string.IsNullOrWhiteSpace(remaining))
                        {
                            if (isNullable)
                                invokeArgs.Add(null);
                            else
                                throw new Exception($"Missing required argument(s).\r\n\r\n{usageStr}");
                        }
                        else
                        {
                            var targetUser = await FoxUser.ParseUser(remaining);
                            if (targetUser is null)
                                throw new Exception($"Could not resolve user: {remaining}\r\n\r\n{usageStr}");
                            invokeArgs.Add(targetUser);
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(remaining))
                        {
                            if (param.HasDefaultValue)
                                invokeArgs.Add(param.DefaultValue);
                            else if (isNullable)
                                invokeArgs.Add(null);
                            else
                                throw new Exception($"Missing required argument(s).\r\n\r\n{usageStr}");
                        }
                        else
                        {
                            invokeArgs.Add(ParseSingleValue(targetType, remaining, paramName));
                        }
                    }
                    break;
                }

                // non-final param → consume only next token
                var nextSpace = remaining.IndexOf(' ');
                string token;
                if (nextSpace == -1)
                {
                    token = remaining;
                    remaining = string.Empty;
                }
                else
                {
                    token = remaining[..nextSpace];
                    remaining = remaining[(nextSpace + 1)..].TrimStart();
                }

                if (string.IsNullOrEmpty(token))
                {
                    if (param.HasDefaultValue)
                    {
                        invokeArgs.Add(param.DefaultValue);
                        continue;
                    }
                    if (isNullable)
                    {
                        invokeArgs.Add(null);
                        continue;
                    }
                    throw new Exception($"Missing required argument(s).\r\n\r\n{usageStr}");
                }

                if (targetType == typeof(string))
                {
                    invokeArgs.Add(token);
                }
                else if (targetType == typeof(FoxUser))
                {
                    var targetUser = await FoxUser.ParseUser(token);
                    if (targetUser is null)
                        throw new Exception($"Could not resolve user: {token}\r\n\r\n{usageStr}");
                    invokeArgs.Add(targetUser);
                }
                else
                {
                    invokeArgs.Add(ParseSingleValue(targetType, token, paramName));
                }
            }

            return invokeArgs.ToArray();
        }

        private static string BuildUsageError(MethodInfo method, string cmd, string? subInput)
        {
            var parameters = method.GetParameters().Skip(3);
            var parts = parameters.Select(p =>
            {
                var name = p.Name ?? p.ParameterType.Name;
                bool optional = IsNullable(p);
                return optional ? $"[<{name}>]" : $"<{name}>";
            });

            string usage = $"/{cmd}";
            //if (!string.IsNullOrEmpty(subInput))
            //    usage += $" {subInput}";
            if (parts.Any())
                usage += " " + string.Join(" ", parts);

            return $"Usage: {usage}";
        }

        private static object ParseSingleValue(Type type, string token, string paramName)
        {
            try
            {
                if (type == typeof(int))
                    if (int.TryParse(token, out var i)) return i;
                if (type == typeof(uint))
                    if (uint.TryParse(token, out var ui)) return ui;
                if (type == typeof(long))
                    if (long.TryParse(token, out var l)) return l;
                if (type == typeof(ulong))
                    if (ulong.TryParse(token, out var ul)) return ul;
                if (type == typeof(float))
                    if (float.TryParse(token, out var f)) return f;
                if (type == typeof(bool))
                    if (bool.TryParse(token, out var b)) return b;
            }
            catch { }
            throw new Exception($"Could not parse '{token}' as {type.Name} for <{paramName}>");
        }

        private static string BuildUsage(string cmd, Dictionary<string, CommandEntry> subs)
        {
            // if there's exactly one handler (no subs), show its full signature
            if (subs.Count == 1 && subs.ContainsKey(string.Empty))
            {
                var method = subs[string.Empty].Method;
                return BuildUsageError(method, cmd, null);
            }

            // otherwise, show usage for each subcommand
            var lines = new List<string>();
            foreach (var kvp in subs.OrderBy(k => k.Key))
            {
                var sub = kvp.Key;
                var method = kvp.Value.Method;
                var parameters = method.GetParameters().Skip(3); // skip t, user, message

                var parts = parameters.Select(p =>
                {
                    var name = p.Name ?? p.ParameterType.Name;
                    bool optional = IsNullable(p);
                    return optional ? $"[<{name}>]" : $"<{name}>";
                });

                string usage = $"/{cmd}";
                if (!string.IsNullOrEmpty(sub))
                    usage += $" {sub}";
                if (parts.Any())
                    usage += " " + string.Join(" ", parts);

                lines.Add(usage);
            }

            if (lines.Count == 1)
                return $"Usage: {lines[0]}";

            return "Usage:\n" + string.Join("\n", lines.Select(l => "  " + l));
        }


        private static bool IsNullable(ParameterInfo param)
        {
            // explicit default values mean optional
            if (param.HasDefaultValue) return true;

            var type = param.ParameterType;
            if (!type.IsValueType) // ref type
            {
                // Look for NullableAttribute metadata
                var nullable = param.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().FullName == "System.Runtime.CompilerServices.NullableAttribute");

                if (nullable != null)
                {
                    var flagsField = nullable.GetType().GetField("NullableFlags");
                    if (flagsField != null)
                    {
                        var flags = (byte[]?)flagsField.GetValue(nullable);
                        if (flags != null && flags.Length > 0)
                        {
                            return flags[0] == 2; // 1 = not nullable, 2 = nullable
                        }
                    }
                }

                return false; // assume non-nullable if no attribute
            }

            // Nullable<T>
            return Nullable.GetUnderlyingType(type) != null;
        }

    }
}
