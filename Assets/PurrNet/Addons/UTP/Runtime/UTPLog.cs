using System.Runtime.CompilerServices;
using PurrNet.Logging;

namespace PurrNet.UTP {
    /// <summary>
    /// Different levels of log sensitivity.
    /// </summary>
    public enum LogLevel : int {
        Off,
        Error,
        Warning,
        Info,
        Verbose
    }

    /// <summary>
    /// The logging class for UTP activity.
    /// </summary>
    public static class UTPLog {
        public static LogLevel LoggerLevel { get; set; } = LogLevel.Info;

        public static void Verbose(string message, [CallerFilePath] string filePath = "") {
            if(LoggerLevel >= LogLevel.Verbose) {
                PurrLogger.Log(message, null, default, filePath);
            }
        }

        public static void Info(string message, [CallerFilePath] string filePath = "") {
            if(LoggerLevel >= LogLevel.Info) {
                PurrLogger.Log(message, null, default, filePath);
            }
        }

        public static void Warning(string message, [CallerFilePath] string filePath = "") {
            if(LoggerLevel >= LogLevel.Warning) {
                PurrLogger.LogWarning(message, null, default, filePath);
            }
        }

        public static void Error(string message, [CallerFilePath] string filePath = "") {
            if(LoggerLevel >= LogLevel.Error) {
                PurrLogger.LogError(message, null, default, filePath);
            }
        }
    }
}
