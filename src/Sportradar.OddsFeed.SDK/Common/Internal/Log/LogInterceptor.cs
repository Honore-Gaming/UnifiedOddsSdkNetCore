﻿/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

namespace Sportradar.OddsFeed.SDK.Common.Internal.Log
{
    /// <summary>
    /// A log proxy used to log input and output parameters of a method
    /// </summary>
    internal class LogInterceptor : IInterceptor
    {
        /// <summary>
        /// A Predicate used to filter which class methods may be logged
        /// </summary>
        public Predicate<MethodInfo> Filter
        {
            get => _filter;
            set { _filter = value ?? (m => true); }
        }

        private Predicate<MethodInfo> _filter;
        private readonly LoggerType _defaultLoggerType;
        private readonly bool _canOverrideLoggerType;

        private struct LogProxyPerm
        {
            public bool LogEnabled;
            public MethodInfo MethodInfo;
            public object Result;
            public ILogger Logger;
            public Stopwatch Watch;
        }

        private readonly IDictionary<int, LogProxyPerm> _proxyPerms;

        /// <summary>
        /// Initializes new instance of the <see cref="LogInterceptor"/>
        /// </summary>
        /// <param name="loggerType">A <see cref="LoggerType"/> to be used within the proxy</param>
        /// <param name="canOverrideLoggerType">A value indicating if the <see cref="LoggerType"/> can be overridden with <see cref="LogAttribute"/> on a method or class</param>
        /// <param name="filter">The filter used to filter log messages</param>
        public LogInterceptor(LoggerType loggerType = LoggerType.Execution, bool canOverrideLoggerType = true, Predicate<MethodInfo> filter = null)
        {
            _defaultLoggerType = loggerType;
            _canOverrideLoggerType = canOverrideLoggerType;
            _filter = filter;

            _proxyPerms = new ConcurrentDictionary<int, LogProxyPerm>();
        }

        private void TaskExecutionFinished(Task task)
        {
            LogProxyPerm perm;
            if (!_proxyPerms.TryGetValue(task.Id, out perm))
            {
                Debug.WriteLine($"No perm for task. Id: {task.Id}");
                return;
            }
            var underlyingResultType = "Task->" + perm.Result.GetType().GetProperty("Result")?.PropertyType.Name;

            if (task.IsFaulted)
            {
                var exceptionMsg = "EXCEPTION: ";
                if (task.Exception != null)
                {
                    if (task.Exception.InnerExceptions != null)
                    {
                        exceptionMsg += task.Exception.InnerExceptions[0].ToString();
                    }
                    else
                    {
                        exceptionMsg += task.Exception.ToString();
                    }
                }
                FinishExecution(logEnabled: perm.LogEnabled,
                                methodInfo: perm.MethodInfo,
                                resultTypeName: underlyingResultType,
                                result: exceptionMsg,
                                logger: perm.Logger,
                                watch: perm.Watch,
                                taskId: $"TaskId:{task.Id}, ");
                return;
            }
            var value = perm.Result.GetType().GetProperty("Result")?.GetValue(task);

            FinishExecution(logEnabled: perm.LogEnabled,
                            methodInfo: perm.MethodInfo,
                            resultTypeName: underlyingResultType,
                            result: value,
                            logger: perm.Logger,
                            watch: perm.Watch,
                            taskId: $"TaskId:{task.Id}, ");
            _proxyPerms.Remove(task.Id);
        }

        private static void FinishExecution(bool logEnabled,
                                            MethodInfo methodInfo,
                                            string resultTypeName,
                                            object result,
                                            ILogger logger,
                                            Stopwatch watch,
                                            string taskId = null)
        {
            watch.Stop();

            if (logEnabled)
            {
                logger.LogInformation($"{taskId}Finished executing '{methodInfo.Name}'. Time: {watch.ElapsedMilliseconds} ms.");
            }

            if (logEnabled && !string.Equals(methodInfo.ReturnType.FullName, "System.Void"))
            {
                var responseMessage = result as HttpResponseMessage;
                if (responseMessage != null)
                {
                    logger.LogDebug($"{taskId}{methodInfo.Name} result: {resultTypeName}={WriteHttpResponseMessage(responseMessage)}");
                }
                else
                {
                    logger.LogDebug($"{taskId}{methodInfo.Name} result: {resultTypeName}={result};");
                }
            }
        }

        public void Intercept(IInvocation invocation)
        {
            var logEnabled = false;
            if (invocation == null || invocation.Method == null)
            {
                throw new ArgumentException("Input parameter 'msg' does not have MethodBase as MethodInfo.");
            }

            var methodInfo = invocation.Method;

            var logger = SdkLoggerFactory.GetLogger(methodInfo.ReflectedType, SdkLoggerFactory.SdkLogRepositoryName, _defaultLoggerType);

            if (_filter != null && _filter(methodInfo))
            {
                logEnabled = true;
            }

            if (!logEnabled || _canOverrideLoggerType)
            {
                var attributes = methodInfo.GetCustomAttributes(true).ToList();
                if (methodInfo.DeclaringType != null)
                {
                    attributes.AddRange(methodInfo.DeclaringType.GetCustomAttributes(true));
                }

                if (attributes.Count > 0)
                {
                    foreach (var t in attributes)
                    {
                        if (!(t is LogAttribute))
                        {
                            continue;
                        }
                        logEnabled = true;
                        if (_canOverrideLoggerType)
                        {
                            logger = SdkLoggerFactory.GetLogger(methodInfo.ReflectedType, SdkLoggerFactory.SdkLogRepositoryName, ((LogAttribute)t).LoggerType);
                        }
                        break;
                    }
                }
            }

            var watch = new Stopwatch();
            watch.Start();

            try
            {
                if (methodInfo.Name == "GetType")
                {
                    logEnabled = false;
                }
                if (logEnabled)
                {
                    logger.LogInformation($"Starting executing '{methodInfo.Name}' ...");
                }

                var methodCall = $"{methodInfo.Name}()";
                if (invocation.Arguments != null && invocation.Arguments.Any())
                {
                    methodCall = $"{methodInfo.Name}({string.Join(",", invocation.Arguments.Select(s=> $"{s?.GetType().Name}={s}"))})";
                }
                if (logEnabled)
                {
                    logger.LogDebug($"Invoking '{methodCall}' ...");
                }

                invocation.Proceed();// MAIN EXECUTION

                if (invocation.ReturnValue is Task task)
                {
                    var perm = new LogProxyPerm
                    {
                        LogEnabled = logEnabled,
                        Logger = logger,
                        MethodInfo = methodInfo,
                        Result = invocation.ReturnValue,
                        Watch = watch
                    };
                    _proxyPerms.Add(task.Id, perm);
                    if (logEnabled)
                    {
                        logger.LogDebug($"TaskId:{task.Id} is executing and we wait to finish ...");
                    }
                    task.ContinueWith(TaskExecutionFinished);
                }
                else
                {
                    FinishExecution(logEnabled, methodInfo, invocation.ReturnValue?.GetType().Name, invocation.ReturnValue, logger, watch);
                }
            }
            catch (Exception e)
            {
                watch.Stop();
                if (logEnabled)
                {
                    logger.LogError($"Exception during executing '{methodInfo.Name}': {Environment.NewLine}", e);
                }

                throw;
            }
        }

        private static string WriteHttpResponseMessage(HttpResponseMessage message)
        {
            if (message == null)
            {
                return null;
            }
            return $"StatusCode: {message.StatusCode}, ReasonPhrase: '{message.ReasonPhrase}', Version: {message.Version}, Content: {message.Content}";
        }

        private static string GetMethodArgumentType(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.Name.StartsWith("Nullable", StringComparison.InvariantCultureIgnoreCase))
            {
                //"System.Nullable`1[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]"
                var t = type.FullName?.Substring(type.FullName.IndexOf("[", StringComparison.InvariantCultureIgnoreCase) + 2);
                if (t != null)
                {
                    t = t.Substring(0, t.IndexOf(",", StringComparison.InvariantCultureIgnoreCase));
                    if (t.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase) > 0)
                    {
                        t = t.Substring(t.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase) + 1);
                    }

                    return t + "?";
                }
            }

            if (type.Name.StartsWith("List", StringComparison.InvariantCultureIgnoreCase))
            {
                var t = type.FullName?.Substring(type.FullName.IndexOf("[", StringComparison.InvariantCultureIgnoreCase) + 1);
                if (t != null)
                {
                    t = t.Substring(0, t.IndexOf(",", StringComparison.InvariantCultureIgnoreCase));
                    if (t.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase) > 0)
                    {
                        t = t.Substring(t.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase));
                    }

                    return $"List<{t}>";
                }
            }

            return type.Name;
        }
    }
}
