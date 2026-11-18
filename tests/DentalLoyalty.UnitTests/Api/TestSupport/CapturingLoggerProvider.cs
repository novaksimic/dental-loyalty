using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DentalLoyalty.UnitTests.Api.TestSupport;

// Collects every log entry the test host writes, so tests can assert what is
// (and is not) logged.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(logLevel, category, formatter(state, exception), exception));
    }
}
