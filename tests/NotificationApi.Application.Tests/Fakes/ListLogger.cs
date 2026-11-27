using Microsoft.Extensions.Logging;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Keeps every message with its level and structured values, to check what is (and isn't) logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Text)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;
        Entries.Add((logLevel, $"{formatter(state, exception)} [{values}]"));
    }
}
