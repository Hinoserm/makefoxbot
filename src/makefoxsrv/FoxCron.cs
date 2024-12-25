using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CronAttribute : Attribute
    {
        public TimeSpan Interval { get; }

        public CronAttribute(int seconds = 0, int minutes = 0, int hours = 0)
        {
            Interval = new TimeSpan(hours, minutes, seconds);
        }
    }

    internal class FoxCron
    {
        private static readonly ConcurrentDictionary<MethodInfo, Task> _tasks = new();
        private static readonly ConcurrentDictionary<MethodInfo, DateTime?> _taskStartTimes = new(); // Track task start times
        private static readonly ConcurrentDictionary<MethodInfo, DateTime?> _taskEndTimes = new();   // Track task end times

        // Non-nullable because we always instantiate an internal token source.
        private static CancellationTokenSource _internalCancellationTokenSource = new();

        // Nullable because, until `Start` is called, we don’t create or link tokens.
        private static CancellationTokenSource? _linkedCancellationTokenSource;

        public static void Start(CancellationToken? cancellationToken = null)
        {
            if (_linkedCancellationTokenSource is not null)
                throw new InvalidOperationException("FoxCron is already running. Cannot start again.");

            // Merge external token (if provided) with the internal token source.
            _linkedCancellationTokenSource = cancellationToken != null
                ? CancellationTokenSource.CreateLinkedTokenSource(_internalCancellationTokenSource.Token, cancellationToken.Value)
                : _internalCancellationTokenSource;

            var methodsWithCron = Assembly.GetExecutingAssembly()
                .GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<CronAttribute>() != null);

            foreach (var method in methodsWithCron)
            {
                var cronAttribute = method.GetCustomAttribute<CronAttribute>();
                if (cronAttribute is null)
                    throw new InvalidOperationException("Method should have a CronAttribute");

                StartCronTask(method, cronAttribute.Interval, _linkedCancellationTokenSource.Token);
            }
        }

        public static void Stop()
        {
            if (_linkedCancellationTokenSource is null)
                throw new InvalidOperationException("FoxCron is not running. Cannot stop.");

            // Cancel the internal token
            _internalCancellationTokenSource.Cancel();

            try
            {
                Task.WhenAll(_tasks.Values).Wait();
            }
            catch (AggregateException ex)
            {
                foreach (var inner in ex.InnerExceptions)
                {
                    FoxLog.LogException(inner);
                }
            }

            _linkedCancellationTokenSource = null;
            _internalCancellationTokenSource.Dispose();
            _internalCancellationTokenSource = new CancellationTokenSource();
            _tasks.Clear();
            _taskStartTimes.Clear();
            _taskEndTimes.Clear();
        }

        private static void StartCronTask(MethodInfo method, TimeSpan interval, CancellationToken token)
        {
            if (!_tasks.ContainsKey(method))
            {
                var task = Task.Run(async () =>
                {
                    FoxContextManager.Current = new FoxContext();

                    while (!token.IsCancellationRequested)
                    {
                        DateTime startTime = DateTime.Now;
                        DateTime? endTime = null;

                        try
                        {
                            // Record the start time for this execution
                            _taskStartTimes[method] = startTime;

                            var instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                            var parameters = method.GetParameters();

                            // If the method’s first parameter is a CancellationToken, pass it in.
                            object?[]? args = null;
                            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(CancellationToken))
                                args = new object?[] { token };

                            // Check if the method is asynchronous
                            var returnType = method.ReturnType;
                            if (returnType == typeof(Task))
                            {
                                // Await the asynchronous method
                                var taskResult = (Task?)method.Invoke(instance, args);
                                if (taskResult is not null)
                                {
                                    await taskResult;
                                }
                            }
                            else if (returnType == typeof(void))
                            {
                                // Invoke synchronous method
                                method.Invoke(instance, args);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Unsupported return type '{returnType}' for method {method.Name}. Only 'void' or 'Task' are supported.");
                            }

                            // Set end time after execution
                            endTime = DateTime.Now;

                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex);
                        }
                        finally
                        {
                            _taskEndTimes[method] = endTime;
                        }

                        try
                        {
                            if (endTime is not null)
                            {
                                var elapsed = endTime - startTime;
                                if (elapsed > interval)
                                {
                                    throw new Exception($"Error: Task {method.Name} exceeded its interval duration of {interval} by {elapsed}.");
                                }
                                else
                                {
                                    FoxLog.WriteLine($"Task {method.Name} completed in {elapsed}.");
                                    await Task.Delay(interval - elapsed.Value, token);
                                }
                            }
                            else
                                await Task.Delay(interval, token);
                        }
                        catch (TaskCanceledException)
                        {
                            // The delay got canceled, so exit the loop gracefully.
                            break;
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex);
                        }
                    }

                    FoxContextManager.Clear();
                }, token);

                _tasks[method] = task;
            }
        }
    }
}
