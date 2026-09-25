using System;
using System.IO;
using System.Text;

namespace NihongoVocab.Services
{
    public static class CrashLogger
    {
        private static readonly object LogLock = new();
        private static readonly string LogDirectory;
        private static readonly string LogFilePath;

        static CrashLogger()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NihongoVocab");

            LogDirectory = Path.Combine(appDataDir, "logs");
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            LogFilePath = Path.Combine(LogDirectory, "crash.log");
        }

        public static void LogInfo(string msg)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] {msg}\r\n", Encoding.UTF8);
                }
            }
            catch {}
        }

        public static void LogException(Exception ex, string context = "Global")
        {
            try
            {
                lock (LogLock)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"[Timestamp] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"[Context] {context}");
                    sb.AppendLine($"[Exception Type] {ex.GetType().FullName}");
                    sb.AppendLine($"[Message] {ex.Message}");
                    sb.AppendLine($"[StackTrace]");
                    sb.AppendLine(ex.StackTrace ?? "No StackTrace available.");

                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"[InnerException Type] {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"[InnerException Message] {ex.InnerException.Message}");
                        sb.AppendLine($"[InnerException StackTrace]");
                        sb.AppendLine(ex.InnerException.StackTrace ?? "No InnerException StackTrace.");
                    }

                    sb.AppendLine("==================================================");
                    sb.AppendLine();

                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败时避免二次抛出
            }
        }

        public static void LogMessage(string message, string level = "INFO")
        {
            try
            {
                lock (LogLock)
                {
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, logLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static string GetLogPath() => LogFilePath;
    }
}
