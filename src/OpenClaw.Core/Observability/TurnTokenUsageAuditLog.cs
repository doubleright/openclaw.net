using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenClaw.Core.Abstractions;
using OpenClaw.Core.Models;

namespace OpenClaw.Core.Observability;

/// <summary>
/// Thread-safe, append-only JSON-lines writer for per-turn token usage.
/// </summary>
public sealed class TurnTokenUsageAuditLog : ITurnTokenUsageObserver, IDisposable
{
    private const int DefaultAuditQueueCapacity = 4096;
    private readonly string? _filePath;
    private readonly ILogger<TurnTokenUsageAuditLog>? _logger;
    private readonly Channel<string>? _lineChannel;
    private readonly Task? _writerTask;
    private int _disposed;

    public TurnTokenUsageAuditLog(
        string? filePath,
        ILogger<TurnTokenUsageAuditLog>? logger = null,
        int auditQueueCapacity = DefaultAuditQueueCapacity)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            _filePath = fullPath;

            _lineChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(Math.Max(1, auditQueueCapacity))
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _writerTask = Task.Run(() => WriteLoopAsync(fullPath, _lineChannel.Reader));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize turn token usage audit path for {Path}; file logging will be disabled", filePath);
            _filePath = null;
        }
    }

    public void RecordTurn(TurnTokenUsageRecord record)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var filePath = _filePath;
        var lineChannel = _lineChannel;
        if (string.IsNullOrWhiteSpace(filePath) || lineChannel is null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(record, TurnTokenUsageJsonContext.Default.TurnTokenUsageRecord);
            if (!lineChannel.Writer.TryWrite(json))
                lineChannel.Writer.WriteAsync(json).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to append turn token usage entry to {Path}", filePath);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_lineChannel is null)
            return;

        _lineChannel.Writer.TryComplete();

        if (_writerTask is null)
            return;

        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to flush turn token usage audit log during disposal");
        }
    }

    private async Task WriteLoopAsync(string filePath, ChannelReader<string> reader)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);

        await foreach (var line in reader.ReadAllAsync())
        {
            try
            {
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to append turn token usage entry to {Path}", filePath);
            }
        }

        await writer.FlushAsync();
    }
}

public sealed class CompositeTurnTokenUsageObserver : ITurnTokenUsageObserver
{
    private readonly IReadOnlyList<ITurnTokenUsageObserver> _observers;
    private readonly ILogger<CompositeTurnTokenUsageObserver>? _logger;

    public CompositeTurnTokenUsageObserver(
        IReadOnlyList<ITurnTokenUsageObserver> observers,
        ILogger<CompositeTurnTokenUsageObserver>? logger = null)
    {
        _observers = observers;
        _logger = logger;
    }

    public void RecordTurn(TurnTokenUsageRecord record)
    {
        foreach (var observer in _observers)
        {
            try
            {
                observer.RecordTurn(record);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Turn token usage observer {ObserverType} failed while recording session {SessionId}",
                    observer.GetType().FullName,
                    record.SessionId);
            }
        }
    }
}

[JsonSerializable(typeof(TurnTokenUsageRecord))]
internal sealed partial class TurnTokenUsageJsonContext : JsonSerializerContext;
