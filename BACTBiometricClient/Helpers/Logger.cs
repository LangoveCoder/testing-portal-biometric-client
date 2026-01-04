using System;
using System.IO;
using System.Text;

namespace BACTBiometricClient.Helpers
{
    /// <summary>
    /// Simple file-based logger for application events and errors
    /// </summary>
    public static class Logger
    {
        private static readonly string _logDirectory;
        private static readonly object _lockObject = new object();

        static Logger()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appDataPath, "BACTBiometric", "Logs");

            // Create logs directory if it doesn't exist
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>
        /// Log an information message
        /// </summary>
        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message)
        {
            WriteLog("WARNING", message);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        /// <summary>
        /// Log an error with exception details
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            var fullMessage = new StringBuilder();
            fullMessage.AppendLine(message);
            fullMessage.AppendLine($"Exception: {ex.Message}");
            fullMessage.AppendLine($"Stack Trace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                fullMessage.AppendLine($"Inner Exception: {ex.InnerException.Message}");
            }

            WriteLog("ERROR", fullMessage.ToString());
        }

        /// <summary>
        /// Log a debug message (only in debug mode)
        /// </summary>
        public static void Debug(string message)
        {
#if DEBUG
            WriteLog("DEBUG", message);
#endif
        }

        /// <summary>
        /// Write log entry to file
        /// </summary>
        private static void WriteLog(string level, string message)
        {
            try
            {
                lock (_lockObject)
                {
                    string logFileName = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
                    string logFilePath = Path.Combine(_logDirectory, logFileName);

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logEntry = $"[{timestamp}] [{level}] {message}";

                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine);

                    // Also write to debug output in Visual Studio
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
            }
            catch
            {
                // Fail silently - logging should never crash the app
            }
        }

        /// <summary>
        /// Get the path to today's log file
        /// </summary>
        public static string GetTodayLogFile()
        {
            string logFileName = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
            return Path.Combine(_logDirectory, logFileName);
        }

        /// <summary>
        /// Get the logs directory path
        /// </summary>
        public static string GetLogsDirectory()
        {
            return _logDirectory;
        }

        /// <summary>
        /// Delete old log files (older than specified days)
        /// </summary>
        public static void CleanOldLogs(int daysToKeep = 30)
        {
            try
            {
                var files = Directory.GetFiles(_logDirectory, "log_*.txt");
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Error("Failed to clean old logs", ex);
            }
        }

        /// <summary>
        /// Clear all log files
        /// </summary>
        public static void ClearAllLogs()
        {
            try
            {
                var files = Directory.GetFiles(_logDirectory, "log_*.txt");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Error("Failed to clear logs", ex);
            }
        }
    }
}