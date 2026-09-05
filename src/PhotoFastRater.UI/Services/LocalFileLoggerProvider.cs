using Microsoft.Extensions.Logging;

namespace PhotoFastRater.UI.Services;

/// <summary>Writes structured application diagnostics to one daily local file without retaining messages in memory.</summary>
public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;

    /// <summary>Opens an append-only UTF-8 log; logging becomes a no-op if the directory is unavailable.</summary>
    public LocalFileLoggerProvider(string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _writer = null;
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(categoryName, this);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
            _writer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (_writer is null)
            return;
        lock (_gate)
        {
            _writer.Write(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            _writer.Write('\t');
            _writer.Write(level);
            _writer.Write('\t');
            _writer.Write(category);
            _writer.Write('\t');
            _writer.Write(eventId.Id);
            _writer.Write('\t');
            _writer.WriteLine(message.ReplaceLineEndings("\\n"));
            if (exception is not null)
                _writer.WriteLine(exception.ToString().ReplaceLineEndings("\\n"));
        }
    }

    private sealed class LocalFileLogger(string category, LocalFileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}
