using Lab.Shared.Configuration;
using Lab.Shared.IO;
using Microsoft.Extensions.Logging;

namespace Lab.Shared.Logging;

public sealed class LabOperationalFileLoggerProvider(EnvironmentLayout layout, SafeFileAppender fileAppender) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new LabOperationalFileLogger(layout.ServiceLogPath, categoryName, fileAppender);

    public void Dispose()
    {
    }

    private sealed class LabOperationalFileLogger(string path, string categoryName, SafeFileAppender fileAppender) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            string line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {Sanitize(message)}";

            if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
            {
                line += $" (EventId={eventId.Id}:{eventId.Name})";
            }

            if (exception is not null)
            {
                line += $" | Exception={Sanitize(exception.ToString())}";
            }

            fileAppender.AppendLine(path, line);
        }

        private static string Sanitize(string value) =>
            value.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
