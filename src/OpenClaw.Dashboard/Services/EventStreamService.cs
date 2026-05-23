using System.Text;

namespace OpenClaw.Dashboard.Services;

public class EventStreamService : IAsyncDisposable
{
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _streamTask;

    public EventStreamService(HttpClient http)
    {
        _http = http;
    }

    public bool IsConnected { get; private set; }

    public async Task StartAsync(string url, Action<string, string> onEvent, CancellationToken ct)
    {
        await StopAsync().ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _streamTask = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("text/event-stream");

                using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    IsConnected = false;
                    return;
                }

                IsConnected = true;

                using var stream = await response.Content
                    .ReadAsStreamAsync(token)
                    .ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var eventType = "message";
                var dataBuffer = new StringBuilder();

                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        if (dataBuffer.Length > 0)
                        {
                            var data = dataBuffer.ToString().TrimEnd('\n');
                            try
                            {
                                onEvent(eventType, data);
                            }
                            catch
                            {
                                // ignore handler errors
                            }
                        }

                        eventType = "message";
                        dataBuffer.Clear();
                        continue;
                    }

                    if (line.StartsWith(":", StringComparison.Ordinal))
                    {
                        // comment / keepalive
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.Ordinal))
                    {
                        eventType = line[6..].Trim();
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        var value = line[5..];
                        if (value.StartsWith(" ", StringComparison.Ordinal))
                        {
                            value = value[1..];
                        }

                        if (dataBuffer.Length > 0)
                        {
                            dataBuffer.Append('\n');
                        }

                        dataBuffer.Append(value);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on stop
            }
            catch
            {
                // swallow stream errors
            }
            finally
            {
                IsConnected = false;
            }
        }, token);
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var task = _streamTask;
        _cts = null;
        _streamTask = null;

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        cts?.Dispose();
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
