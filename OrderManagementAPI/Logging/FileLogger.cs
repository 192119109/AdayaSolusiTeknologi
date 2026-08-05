using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;

namespace OrderManagementAPI.Logging
{
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string?> _correlationId = new();

        public static string? CorrelationId
        {
            get => _correlationId.Value;
            set => _correlationId.Value = value;
        }
    }

    public class FileLogger : ILogger
    {
        private readonly string _filePath;
        private static readonly object _lock = new();

        public FileLogger(string filePath)
        {
            _filePath = filePath;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var correlationId = CorrelationContext.CorrelationId;
            var logRecord = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}]";
            
            if (!string.IsNullOrEmpty(correlationId))
            {
                logRecord += $" [CorrelationId: {correlationId}]";
            }
            
            logRecord += $" {message}";
            
            if (exception != null)
            {
                logRecord += Environment.NewLine + exception;
            }

            lock (_lock)
            {
                File.AppendAllText(_filePath, logRecord + Environment.NewLine);
            }
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;

        public FileLoggerProvider(string filePath)
        {
            _filePath = filePath;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(_filePath);
        }

        public void Dispose() { }
    }
}
