using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using OpenClaw.Channels;
using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;

namespace OpenClaw.Gateway;

internal sealed record CanvasCommandResult(
    bool Success,
    string RequestId,
    string? Error = null,
    string? SnapshotJson = null,
    string? ValueJson = null);

internal sealed class CanvasCommandBroker
{
    private readonly GatewayConfig _config;
    private readonly WebSocketChannel _webSocketChannel;
    private readonly RuntimeEventStore _runtimeEvents;
    private readonly ConcurrentDictionary<string, PendingCommand> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ClientCanvasProfile> _clientProfiles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SurfaceCatalogKey, string> _surfaceCatalogs = new();

    public CanvasCommandBroker(GatewayConfig config, WebSocketChannel webSocketChannel, RuntimeEventStore runtimeEvents)
    {
        _config = config;
        _webSocketChannel = webSocketChannel;
        _runtimeEvents = runtimeEvents;
        _webSocketChannel.OnCanvasClientEnvelopeReceived += HandleClientEnvelopeAsync;
    }

    public IReadOnlyList<string> GetClientCapabilities(string senderId)
        => _clientProfiles.TryGetValue(senderId, out var profile) ? profile.Capabilities : [];

    public IReadOnlyList<string> GetClientSupportedCatalogIds(string senderId)
        => _clientProfiles.TryGetValue(senderId, out var profile) ? profile.SupportedCatalogIds : [];

    public bool TryChooseCatalog(
        string senderId,
        string? requestedCatalogId,
        out A2UiCatalogDescriptor? catalog,
        out string? error)
    {
        catalog = null;
        error = null;
        var supportedCatalogIds = GetClientSupportedCatalogIds(senderId);

        if (!string.IsNullOrWhiteSpace(requestedCatalogId))
        {
            if (!A2UiCatalogRegistry.TryGetCatalog(requestedCatalogId, out var requested))
            {
                error = $"Unknown A2UI catalog '{requestedCatalogId}'.";
                return false;
            }

            if (supportedCatalogIds.Count > 0 && !supportedCatalogIds.Contains(requested.CatalogId, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Canvas client does not support catalog '{requested.CatalogId}'.";
                return false;
            }

            catalog = requested;
            return true;
        }

        if (!A2UiCatalogRegistry.TryChooseCatalog(supportedCatalogIds, null, out var chosen))
        {
            error = "No compatible A2UI catalog is available.";
            return false;
        }

        catalog = chosen;
        return true;
    }

    public bool TryGetSurfaceCatalogId(string senderId, string sessionId, string surfaceId, out string? catalogId)
        => _surfaceCatalogs.TryGetValue(new SurfaceCatalogKey(senderId, sessionId, surfaceId), out catalogId);

    public async Task<CanvasCommandResult> SendCommandAsync(
        Session session,
        WsServerEnvelope command,
        string expectedResponseType,
        string? requiredCapability,
        CancellationToken ct)
    {
        if (!_config.Canvas.Enabled)
            return Fail(command.RequestId, "Canvas is disabled.");

        if (!string.Equals(session.ChannelId, "websocket", StringComparison.Ordinal))
            return Fail(command.RequestId, "Canvas commands require an active websocket session.");

        if (!_webSocketChannel.IsClientConnected(session.SenderId) || !_webSocketChannel.IsClientUsingEnvelopes(session.SenderId))
            return Fail(command.RequestId, "Canvas client is not connected in websocket envelope mode.");

        if (!string.IsNullOrWhiteSpace(requiredCapability) && !HasCapability(session.SenderId, requiredCapability))
        {
            var advertised = GetClientCapabilities(session.SenderId);
            return Fail(
                command.RequestId,
                advertised.Count == 0
                    ? "Canvas client has not advertised capabilities yet."
                    : $"Canvas client does not support capability '{requiredCapability}'.");
        }

        var requestId = string.IsNullOrWhiteSpace(command.RequestId)
            ? $"canvas_{Guid.NewGuid():N}"[..24]
            : command.RequestId!;

        var surfaceId = string.IsNullOrWhiteSpace(command.SurfaceId) ? "main" : command.SurfaceId!;
        var outbound = command with
        {
            RequestId = requestId,
            SessionId = session.Id,
            SurfaceId = surfaceId
        };

        string? lockedCatalogId = null;
        if (IsCatalogLockedUpdate(outbound.Type) &&
            _surfaceCatalogs.TryGetValue(new SurfaceCatalogKey(session.SenderId, session.Id, surfaceId), out lockedCatalogId) &&
            !string.IsNullOrWhiteSpace(outbound.CatalogId) &&
            !string.Equals(lockedCatalogId, outbound.CatalogId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(requestId, $"Canvas surface '{surfaceId}' is locked to catalog '{lockedCatalogId}' and cannot use catalog '{outbound.CatalogId}'.");
        }

        var negotiationCatalogId = string.IsNullOrWhiteSpace(outbound.CatalogId) ? lockedCatalogId : outbound.CatalogId;
        if (IsCatalogAwareCommand(outbound.Type) && !TryChooseCatalog(session.SenderId, negotiationCatalogId, out _, out var catalogError))
            return Fail(requestId, catalogError ?? "Canvas client does not support the requested catalog.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(outbound, CoreJsonContext.Default.WsServerEnvelope);
        if (bytes.Length > Math.Max(1, _config.Canvas.MaxCommandBytes))
            return Fail(requestId, $"Canvas command exceeds {_config.Canvas.MaxCommandBytes} bytes.");

        var pending = new PendingCommand(requestId, session.Id, session.SenderId, expectedResponseType);
        if (!_pending.TryAdd(requestId, pending))
            return Fail(requestId, "Canvas command request id collision.");

        AppendRuntimeEvent(session, outbound.Type, requestId, "sent", "info", $"Sent Canvas command '{outbound.Type}'.");

        try
        {
            await _webSocketChannel.SendEnvelopeAsync(session.SenderId, outbound, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_config.Canvas.CommandTimeoutSeconds, 1, 300)));
            var response = await pending.Task.WaitAsync(timeoutCts.Token);

            if (!string.Equals(response.Type, expectedResponseType, StringComparison.Ordinal))
                return Fail(requestId, $"Unexpected Canvas response '{response.Type}'.");

            if (!string.IsNullOrWhiteSpace(response.SnapshotJson) &&
                Encoding.UTF8.GetByteCount(response.SnapshotJson) > Math.Max(1, _config.Canvas.MaxSnapshotBytes))
            {
                return Fail(requestId, $"Canvas snapshot exceeds {_config.Canvas.MaxSnapshotBytes} bytes.");
            }

            if (response.Success == false || !string.IsNullOrWhiteSpace(response.Error))
            {
                var error = string.IsNullOrWhiteSpace(response.Error) ? "Canvas command failed." : response.Error!;
                AppendRuntimeEvent(session, outbound.Type, requestId, "failed", "warning", error);
                return Fail(requestId, error);
            }

            UpdateSurfaceCatalogLockAfterSuccess(session, outbound);

            AppendRuntimeEvent(session, outbound.Type, requestId, "completed", "info", $"Canvas command '{outbound.Type}' completed.");
            return new CanvasCommandResult(true, requestId, SnapshotJson: response.SnapshotJson, ValueJson: response.ValueJson);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            AppendRuntimeEvent(session, outbound.Type, requestId, "timed_out", "warning", $"Canvas command '{outbound.Type}' timed out.");
            return Fail(requestId, "Canvas command timed out.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    internal ValueTask HandleClientEnvelopeAsync(string clientId, WsClientEnvelope envelope, CancellationToken ct)
    {
        if (string.Equals(envelope.Type, "canvas_ready", StringComparison.Ordinal))
        {
            _clientProfiles[clientId] = new ClientCanvasProfile(
                NormalizeCapabilities(envelope.Capabilities),
                NormalizeCatalogIds(envelope.SupportedCatalogIds));
            return ValueTask.CompletedTask;
        }

        if (envelope.Capabilities is { Length: > 0 } || envelope.SupportedCatalogIds is { Length: > 0 })
        {
            _clientProfiles.AddOrUpdate(
                clientId,
                _ => new ClientCanvasProfile(
                    NormalizeCapabilities(envelope.Capabilities),
                    NormalizeCatalogIds(envelope.SupportedCatalogIds)),
                (_, existing) => existing with
                {
                    Capabilities = envelope.Capabilities is { Length: > 0 }
                        ? NormalizeCapabilities(envelope.Capabilities)
                        : existing.Capabilities,
                    SupportedCatalogIds = envelope.SupportedCatalogIds is { Length: > 0 }
                        ? NormalizeCatalogIds(envelope.SupportedCatalogIds)
                        : existing.SupportedCatalogIds
                });
        }

        if (!string.IsNullOrWhiteSpace(envelope.RequestId) &&
            _pending.TryGetValue(envelope.RequestId, out var pending) &&
            string.Equals(pending.SenderId, clientId, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(envelope.SessionId) &&
                !string.Equals(envelope.SessionId, pending.SessionId, StringComparison.Ordinal))
            {
                pending.TrySetResult(envelope with
                {
                    Success = false,
                    Error = "Canvas response session id did not match the pending command."
                });
                return ValueTask.CompletedTask;
            }

            pending.TrySetResult(envelope);
        }

        return ValueTask.CompletedTask;
    }

    private bool HasCapability(string senderId, string capability)
        => _clientProfiles.TryGetValue(senderId, out var profile) &&
           profile.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);

    private static string[] NormalizeCapabilities(string[]? capabilities)
        => NormalizeStrings(capabilities);

    private static string[] NormalizeCatalogIds(string[]? catalogIds)
        => NormalizeStrings(catalogIds);

    private static string[] NormalizeStrings(string[]? values)
        => values is null
            ? []
            : values
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static bool IsCatalogAwareCommand(string commandType)
        => string.Equals(commandType, "a2ui_create_surface", StringComparison.Ordinal) ||
           string.Equals(commandType, "a2ui_update_components", StringComparison.Ordinal) ||
           string.Equals(commandType, "a2ui_update_data_model", StringComparison.Ordinal) ||
           string.Equals(commandType, "a2ui_delete_surface", StringComparison.Ordinal) ||
           string.Equals(commandType, "a2ui_sync_ui_to_data", StringComparison.Ordinal);

    private static bool IsCatalogLockedUpdate(string commandType)
        => string.Equals(commandType, "a2ui_update_components", StringComparison.Ordinal) ||
           string.Equals(commandType, "a2ui_update_data_model", StringComparison.Ordinal);

    private void UpdateSurfaceCatalogLockAfterSuccess(Session session, WsServerEnvelope outbound)
    {
        var surfaceId = string.IsNullOrWhiteSpace(outbound.SurfaceId) ? "main" : outbound.SurfaceId!;
        var key = new SurfaceCatalogKey(session.SenderId, session.Id, surfaceId);

        if (string.Equals(outbound.Type, "a2ui_delete_surface", StringComparison.Ordinal))
        {
            _surfaceCatalogs.TryRemove(key, out _);
            return;
        }

        if (!string.Equals(outbound.Type, "a2ui_create_surface", StringComparison.Ordinal))
            return;

        if (!TryChooseCatalog(session.SenderId, outbound.CatalogId, out var catalog, out _) || catalog is null)
            return;

        _surfaceCatalogs[key] = catalog.CatalogId;
    }

    private void AppendRuntimeEvent(Session session, string action, string requestId, string state, string severity, string summary)
    {
        _runtimeEvents.Append(new RuntimeEventEntry
        {
            Id = $"evt_{Guid.NewGuid():N}"[..20],
            SessionId = session.Id,
            ChannelId = session.ChannelId,
            SenderId = session.SenderId,
            Component = "canvas",
            Action = state,
            Severity = severity,
            Summary = summary,
            Metadata = new Dictionary<string, string>
            {
                ["requestId"] = requestId,
                ["command"] = action
            }
        });
    }

    private static CanvasCommandResult Fail(string? requestId, string error)
        => new(false, string.IsNullOrWhiteSpace(requestId) ? "" : requestId!, error);

    private sealed record ClientCanvasProfile(string[] Capabilities, string[] SupportedCatalogIds);

    private sealed record SurfaceCatalogKey(string SenderId, string SessionId, string SurfaceId);

    private sealed class PendingCommand
    {
        private readonly TaskCompletionSource<WsClientEnvelope> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingCommand(string requestId, string sessionId, string senderId, string expectedResponseType)
        {
            RequestId = requestId;
            SessionId = sessionId;
            SenderId = senderId;
            ExpectedResponseType = expectedResponseType;
        }

        public string RequestId { get; }
        public string SessionId { get; }
        public string SenderId { get; }
        public string ExpectedResponseType { get; }
        public Task<WsClientEnvelope> Task => _tcs.Task;

        public void TrySetResult(WsClientEnvelope envelope)
            => _tcs.TrySetResult(envelope);
    }
}
