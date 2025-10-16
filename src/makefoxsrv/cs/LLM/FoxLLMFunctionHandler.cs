#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Swan.Logging;
using static makefoxsrv.FoxLLMConversation;

namespace makefoxsrv
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LLMFunctionAttribute : Attribute
    {
        public string Description { get; }

        public LLMFunctionAttribute(string description)
        {
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class LLMParamAttribute : Attribute
    {
        public string Description { get; }
        public bool Required { get; }
        
        public LLMParamAttribute(string description, bool required = true)
        {
            Description = description;
            Required = required;
        }
    }

    public static class FoxLLMFunctionHandler
    {
        private static readonly Dictionary<string, MethodInfo> _registry = new();

        static FoxLLMFunctionHandler()
        {
            RegisterAll();
        }

        private static void RegisterAll()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var type in asm.GetTypes()
                     .Where(t => t.IsClass && t.Namespace == "makefoxsrv.llm.functions"))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var fnAttr = method.GetCustomAttribute<LLMFunctionAttribute>();
                    if (fnAttr == null)
                        continue;

                    if (_registry.ContainsKey(method.Name))
                        throw new InvalidOperationException($"Duplicate LLM function name: {method.Name}");

                    _registry[method.Name] = method;
                }
            }
        }

        public static IReadOnlyDictionary<string, MethodInfo> RegisteredFunctions => _registry;

        public static JArray BuildToolSchema()
        {
            var tools = new JArray();

            foreach (var kv in _registry)
            {
                var method = kv.Value;
                var fnAttr = method.GetCustomAttribute<LLMFunctionAttribute>()!;
                var parameters = new JObject();
                var required = new List<string>();

                // skip FoxTelegram and FoxUser
                var methodParams = method.GetParameters().Skip(2);

                foreach (var p in methodParams)
                {
                    var paramAttr = p.GetCustomAttribute<LLMParamAttribute>();
                    string desc = paramAttr?.Description ?? $"Parameter of type {p.ParameterType.Name}";
                    var paramSchema = DescribeParameterType(p.ParameterType, desc);
                    parameters[p.Name!] = paramSchema;
                    if (paramAttr?.Required ?? true)
                        required.Add(p.Name!);
                }

                var fnSchema = new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = method.Name,
                        ["description"] = fnAttr.Description,
                        ["parameters"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = parameters,
                            ["required"] = new JArray(required)
                        }
                    }
                };

                tools.Add(fnSchema);
            }

            return tools;
        }


        private static JObject DescribeParameterType(Type t, string description)
        {
            // Return full JSON schema node for this parameter
            if (t == typeof(string))
                return new JObject { ["type"] = "string", ["description"] = description };
            if (t == typeof(bool))
                return new JObject { ["type"] = "boolean", ["description"] = description };
            if (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong))
                return new JObject { ["type"] = "integer", ["description"] = description };
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
                return new JObject { ["type"] = "number", ["description"] = description };

            if (t.IsArray)
            {
                var elem = DescribeParameterType(t.GetElementType()!, "Array element");
                return new JObject
                {
                    ["type"] = "array",
                    ["description"] = description,
                    ["items"] = elem
                };
            }

            if (t.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(t.GetGenericTypeDefinition()))
            {
                var elemType = t.GetGenericArguments()[0];
                var elem = DescribeParameterType(elemType, "List element");
                return new JObject
                {
                    ["type"] = "array",
                    ["description"] = description,
                    ["items"] = elem
                };
            }

            // fallback
            return new JObject { ["type"] = "object", ["description"] = description };
        }

        private static object ConvertTuples(object obj)
        {
            if (obj is System.Collections.IEnumerable enumerable && obj is not string)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                    list.Add(ConvertTuples(item));
                return list;
            }

            var type = obj.GetType();
            if (type.FullName!.StartsWith("System.ValueTuple"))
            {
                var fields = type.GetFields();
                var dict = new Dictionary<string, object?>();
                foreach (var f in fields)
                    dict[f.Name] = ConvertTuples(f.GetValue(obj)!);
                return dict;
            }

            return obj;
        }


        public static async Task RunAllFunctionsAsync(FoxTelegram t, FoxUser user, JToken? toolCalls)
        {
            Console.WriteLine(toolCalls?.ToString(Newtonsoft.Json.Formatting.Indented));

            if (toolCalls == null || !toolCalls.Any())
                return;

            bool doRunLLM = false;

            var payload = new
            {
                tool_calls = toolCalls
            };

            foreach (var call in toolCalls)
            {
                string? callId = call["id"]?.ToString(); // xAI/OpenAI tool_call_id
                string? functionName = call["function"]?["name"]?.ToString();
                string? argumentsJson = call["function"]?["arguments"]?.ToString();

                try
                {
                    if (string.IsNullOrEmpty(functionName))
                        throw new InvalidOperationException("Tool call missing function name.");

                    if (!_registry.TryGetValue(functionName, out var method))
                        throw new InvalidOperationException($"Unknown LLM function: {functionName}");

                    var argsObj = string.IsNullOrWhiteSpace(argumentsJson)
                        ? new JObject()
                        : JObject.Parse(argumentsJson);

                    var parameters = method.GetParameters();
                    var invokeArgs = new List<object?> { t, user };

                    foreach (var p in parameters.Skip(2)) // skip (FoxTelegram, FoxUser)
                    {
                        if (argsObj.TryGetValue(p.Name!, StringComparison.OrdinalIgnoreCase, out var token))
                        {
                            var targetType = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;

                            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(targetType) &&
                                targetType != typeof(string))
                            {
                                if (token.Type == JTokenType.Object)
                                {
                                    var list = new List<string>();
                                    foreach (var child in ((JObject)token).Properties())
                                        list.Add(child.Name);
                                    token = new JArray(list);
                                }
                                else if (token.Type == JTokenType.String)
                                {
                                    token = new JArray(token);
                                }
                            }

                            invokeArgs.Add(token.ToObject(targetType));
                        }
                        else if (p.HasDefaultValue)
                        {
                            invokeArgs.Add(p.DefaultValue);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Missing required parameter: {p.Name}");
                        }
                    }

                    object? result;
                    try
                    {
                        result = method.Invoke(null, invokeArgs.ToArray());
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw tie.InnerException ?? tie;
                    }

                    // Handle async results
                    object? finalResult = null;
                    if (result is Task task)
                    {
                        await task.ConfigureAwait(false);

                        if (task.GetType().IsGenericType)
                        {
                            // Task<T> → extract Result
                            var resProp = task.GetType().GetProperty("Result");
                            finalResult = resProp?.GetValue(task);
                        }
                        else
                        {
                            // Non-generic Task (Task<void>) → no return value
                            finalResult = null;
                        }
                    }
                    else
                    {
                        finalResult = result;
                    }
                    if (functionName != "SendResponse")
                    {
                        // Convert tuples etc. for clean JSON
                        string? jsonResult = finalResult is not null
                            ? Newtonsoft.Json.JsonConvert.SerializeObject(ConvertTuples(finalResult), Newtonsoft.Json.Formatting.Indented)
                            : null;

                        await FoxLLMConversation.SaveFunctionCallAsync(user, callId, functionName, argumentsJson ?? "{}", jsonResult);


                        // Determine if follow-up LLM run should happen
                        bool isVoid = method.ReturnType == typeof(void);
                        bool isTaskVoid = typeof(Task).IsAssignableFrom(method.ReturnType) && !method.ReturnType.IsGenericType;

                        doRunLLM = !(isVoid || isTaskVoid || finalResult is null);
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, $"Error executing LLM function {functionName}: {ex.Message}");

                    // Serialize a clean, structured error for the LLM
                    var errorPayload = new
                    {
                        function = functionName,
                        error = new
                        {
                            message = ex.Message,
                            type = ex.GetType().Name,
                            //stack = ex.StackTrace?.Split('\n')
                        }
                    };

                    string jsonError = Newtonsoft.Json.JsonConvert.SerializeObject(
                        errorPayload,
                        Newtonsoft.Json.Formatting.Indented
                    );

                    // Also persist this to llm_function_calls for traceability
                    await FoxLLMConversation.SaveFunctionCallAsync(
                        user,
                        callId,
                        functionName ?? "Unknown",
                        argumentsJson ?? "{}",
                        jsonError
                    );

                    doRunLLM = true;
                }
            }

            // After loop
            if (doRunLLM)
            {
                await FoxLLM.SendLLMRequest(t, user, null, true);
            }
        }

    }
}
