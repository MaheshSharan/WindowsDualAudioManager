using System.Diagnostics;

namespace AudioDual.Core.Diagnostics
{
    /// <summary>
    /// Writes structured log lines to %AppData%/AudioDual/error_log.txt, the same path the
    /// application's unhandled-exception handler already used. Centralizing here means every
    /// module reports errors through one consistent, inspectable file instead of a console
    /// window most users never open.
    /// </summary>
    public sealed class FileAppLogger : IAppLogger
    {
        private const string LogDirectoryName = "AudioDual";
        private const string LogFileName = "error_log.txt";

        private readonly string _logFilePath;
        private readonly object _writeLock = new();

        public FileAppLogger()
        {
            var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataDirectory, LogDirectoryName);
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, LogFileName);
        }

        public void LogInformation(string category, string message)
        {
            WriteLine("INFO", category, message, exception: null);
        }

        public void LogWarning(string category, string message)
        {
            WriteLine("WARN", category, message, exception: null);
        }

        public void LogError(string category, string message, Exception? exception = null)
        {
            WriteLine("ERROR", category, message, exception);
        }

        private void WriteLine(string level, string category, string message, Exception? exception)
        {
            var line = exception is null
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message}"
                : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message} :: {exception}";

            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // The log path itself is unavailable (disk full, permissions, etc).
                    // There is nowhere left to safely report this, so fall through to the
                    // debug output only — never let logging itself crash the audio pipeline.
                }
            }

            Debug.WriteLine(line);
        }
    }
}
