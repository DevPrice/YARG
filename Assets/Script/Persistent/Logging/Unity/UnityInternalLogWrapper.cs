using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace YARG.Logging.Unity
{
    public static class UnityInternalLogWrapper
    {
        // Reference: https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Runtime/Export/Debug/Debug.bindings.cs

        public static UnityInternalLog UnityInternalLogDelegate { get; private set; } = FallbackLog;

        public static UnityInternalLogException UnityInternalLogExceptionDelegate { get; private set; } = FallbackLogException;

        public static UnityInternalExtractFormattedStackTrace UnityInternalExtractFormattedStackTraceDelegate { get; private set; } = FallbackExtractFormattedStackTrace;

        private static FieldInfo s_LoggerField;

        private static ILogger _originalUnityLogger;

        private static bool _loggerOverridden;

        public static void OverwriteUnityInternals()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

            UnityInternalLog logDelegate = null;
            UnityInternalLogException logExceptionDelegate = null;
            UnityInternalExtractFormattedStackTrace extractStackTraceDelegate = null;

            // The internals below can be renamed by a Unity upgrade or stripped from an IL2CPP player, so a
            // missing member falls back to the public Debug API instead of failing logger setup.
            try
            {
                var debugLogHandler = typeof(Debug).Assembly.GetType("UnityEngine.DebugLogHandler");

                logDelegate = CreateDelegate<UnityInternalLog>(debugLogHandler, "Internal_Log", flags);
                logExceptionDelegate = CreateDelegate<UnityInternalLogException>(debugLogHandler,
                    "Internal_LogException", flags);
                extractStackTraceDelegate = CreateDelegate<UnityInternalExtractFormattedStackTrace>(
                    typeof(StackTraceUtility), "ExtractFormattedStackTrace", flags);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unity internal logging is unavailable, using the public Debug API: {e.Message}");
            }

            UnityInternalLogDelegate = logDelegate ?? FallbackLog;
            UnityInternalLogExceptionDelegate = logExceptionDelegate ?? FallbackLogException;
            UnityInternalExtractFormattedStackTraceDelegate =
                extractStackTraceDelegate ?? FallbackExtractFormattedStackTrace;

            // CustomUnityLogger writes through these delegates. If they are the Debug API fallbacks, installing it
            // as Debug.s_Logger would make every log call recurse into itself.
            if (logDelegate is null || logExceptionDelegate is null)
            {
                return;
            }

            // Override internal Debug.s_Logger to our own
            var debugType = typeof(Debug);
            s_LoggerField = debugType.GetField("s_Logger", flags);

            if (s_LoggerField?.GetValue(null) is not ILogger originalLogger)
            {
                return;
            }

            _originalUnityLogger = originalLogger;

            // We pass in the original ILogHandler to our custom logger because some parts of Unity
            // explicitly check if its their type (DebugLogHandler) for certain exception logging
            s_LoggerField.SetValue(null, new CustomUnityLogger(_originalUnityLogger.logHandler));
            _loggerOverridden = true;
        }

        public static void RestoreUnityInternals()
        {
            if (!_loggerOverridden)
            {
                return;
            }

            var debugType = typeof(Debug);
            var loggerField = s_LoggerField ??
                debugType.GetField("s_Logger", BindingFlags.NonPublic | BindingFlags.Static);

            // Restore original Debug.s_Logger
            if (_originalUnityLogger is not null)
            {
                loggerField.SetValue(null, _originalUnityLogger);
            }
            else
            {
                var loggerType = debugType.Assembly.GetType("UnityEngine.Logger");
                var loggerHandlerType = debugType.Assembly.GetType("UnityEngine.DebugLogHandler");

                var handler = Activator.CreateInstance(loggerHandlerType);
                var logger = Activator.CreateInstance(loggerType, handler);

                loggerField.SetValue(null, logger);
            }

            _loggerOverridden = false;
        }

        private static T CreateDelegate<T>(Type type, string methodName, BindingFlags flags)
            where T : Delegate
        {
            var method = type?.GetMethod(methodName, flags);
            return method?.CreateDelegate(typeof(T)) as T;
        }

        private static void FallbackLog(LogType level, LogOption options, string msg, Object obj)
        {
            Debug.LogFormat(level, options, obj, "{0}", msg);
        }

        private static void FallbackLogException(Exception ex, Object obj)
        {
            Debug.LogException(ex, obj);
        }

        private static string FallbackExtractFormattedStackTrace(StackTrace stackTrace)
        {
            return stackTrace.ToString();
        }

        public delegate void UnityInternalLog(LogType level, LogOption options, string msg, Object obj);

        public delegate void UnityInternalLogException(Exception ex, Object obj);

        public delegate string UnityInternalExtractFormattedStackTrace(StackTrace stackTrace);
    }
}
