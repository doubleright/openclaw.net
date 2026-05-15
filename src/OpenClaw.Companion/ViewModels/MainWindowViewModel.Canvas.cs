using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int MaxSnapshotJsonChars = 128 * 1024;
    private const int MaxSnapshotFrames = 100;
    private const int MaxSnapshotTextChars = 1_024;

    private string? _canvasSessionId;
    private long _canvasEventSequence;

    [ObservableProperty]
    private bool _isCanvasVisible;

    [ObservableProperty]
    private string _canvasStatus = "Canvas idle.";

    [ObservableProperty]
    private string _canvasHtmlStatus = "";

    public ObservableCollection<A2UiSurfaceItem> CanvasSurfaces { get; } = new();

    [ObservableProperty]
    private A2UiSurfaceItem? _activeCanvasSurface;

    public ObservableCollection<A2UiFrameItem> CanvasFrames { get; } = new();

    public bool HasCanvasFrames => CanvasFrames.Count > 0;
    public bool HasCanvasSurfaces => CanvasSurfaces.Count > 0;
    public bool HasActiveCanvasSurfaceDiagnostics => ActiveCanvasSurface?.Diagnostics.Count > 0;
    public bool HasCanvasHtmlStatus => !string.IsNullOrWhiteSpace(CanvasHtmlStatus);

    partial void OnCanvasHtmlStatusChanged(string value)
        => OnPropertyChanged(nameof(HasCanvasHtmlStatus));

    partial void OnActiveCanvasSurfaceChanged(A2UiSurfaceItem? oldValue, A2UiSurfaceItem? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is not null)
            newValue.IsActive = true;
        SyncCompatibilityCanvasFrames();
        OnPropertyChanged(nameof(HasActiveCanvasSurfaceDiagnostics));
    }

    private async Task SendCanvasReadyAsync()
    {
        if (!_client.IsConnected)
            return;

        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "canvas_ready",
            Capabilities =
            [
                "a2ui.v0_8",
                "canvas.present",
                "canvas.hide",
                "snapshot.state",
                "a2ui.v0_9"
            ],
            SupportedCatalogIds =
            [
                A2UiCatalogRegistry.OpenClawV08CatalogId,
                A2UiCatalogRegistry.AGenUiCatalogId
            ]
        }, CancellationToken.None);
    }

    private void HandleCanvasEnvelope(WsServerEnvelope envelope)
    {
        if (!IsCanvasServerEnvelope(envelope.Type))
            return;

        Dispatcher.UIThread.Post(() => _ = ApplyCanvasEnvelopeAsync(envelope));
    }

    private async Task ApplyCanvasEnvelopeAsync(WsServerEnvelope envelope)
    {
        _canvasSessionId = string.IsNullOrWhiteSpace(envelope.SessionId) ? _canvasSessionId : envelope.SessionId;
        var surfaceId = string.IsNullOrWhiteSpace(envelope.SurfaceId) ? "main" : envelope.SurfaceId!;

        try
        {
            switch (envelope.Type)
            {
                case "canvas_present":
                    IsCanvasVisible = true;
                    CanvasStatus = "Canvas visible.";
                    await SendCanvasAckAsync(envelope, success: true, error: null);
                    return;

                case "canvas_hide":
                    IsCanvasVisible = false;
                    CanvasStatus = "Canvas hidden.";
                    await SendCanvasAckAsync(envelope, success: true, error: null);
                    return;

                case "canvas_navigate":
                    await ApplyCanvasNavigateAsync(envelope);
                    return;

                case "a2ui_reset":
                    CanvasSurfaces.Clear();
                    ActiveCanvasSurface = null;
                    CanvasFrames.Clear();
                    OnPropertyChanged(nameof(HasCanvasFrames));
                    OnPropertyChanged(nameof(HasCanvasSurfaces));
                    CanvasStatus = "Canvas reset.";
                    await SendCanvasAckAsync(envelope, success: true, error: null);
                    return;

                case "a2ui_push":
                    await ApplyA2UiPushAsync(envelope, "main");
                    return;

                case "a2ui_create_surface":
                    await ApplyA2UiCreateSurfaceAsync(envelope, surfaceId);
                    return;

                case "a2ui_update_components":
                    await ApplyA2UiUpdateComponentsAsync(envelope, surfaceId);
                    return;

                case "a2ui_update_data_model":
                    await ApplyA2UiUpdateDataModelAsync(envelope, surfaceId);
                    return;

                case "a2ui_delete_surface":
                    await ApplyA2UiDeleteSurfaceAsync(envelope, surfaceId);
                    return;

                case "a2ui_sync_ui_to_data":
                    await SendA2UiSyncResultAsync(envelope);
                    return;

                case "canvas_snapshot":
                    await SendCanvasSnapshotResultAsync(envelope);
                    return;

                case "a2ui_eval":
                    await SendCanvasEvalResultAsync(envelope, success: false, valueJson: null, error: "Companion native Canvas does not support A2UI eval.");
                    return;
            }
        }
        catch (Exception ex)
        {
            await SendCanvasAckAsync(envelope, success: false, error: ex.Message);
        }
    }

    private async Task ApplyCanvasNavigateAsync(WsServerEnvelope envelope)
    {
        if (string.Equals(envelope.Url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            CanvasSurfaces.Clear();
            ActiveCanvasSurface = null;
            CanvasFrames.Clear();
            CanvasHtmlStatus = "";
            CanvasStatus = "Canvas navigated to blank.";
            OnPropertyChanged(nameof(HasCanvasFrames));
            OnPropertyChanged(nameof(HasCanvasSurfaces));
            await SendCanvasAckAsync(envelope, success: true, error: null);
            return;
        }

        var error = !string.IsNullOrWhiteSpace(envelope.Html)
            ? "Companion native Canvas does not support local HTML navigation without a WebView."
            : "Companion native Canvas only supports A2UI frames and about:blank navigation.";
        CanvasHtmlStatus = error;
        CanvasStatus = error;
        await SendCanvasAckAsync(envelope, success: false, error: error);
    }

    private async Task ApplyA2UiPushAsync(WsServerEnvelope envelope, string surfaceId)
    {
        var validation = A2UiFrameValidator.ValidateJsonl(envelope.Frames, maxFrames: 1_000, maxBytes: 512 * 1024);
        if (!validation.IsValid)
        {
            await SendCanvasAckAsync(envelope, success: false, error: validation.Error);
            return;
        }

        var surface = GetOrCreateSurface(surfaceId, A2UiCatalogRegistry.OpenClawV08CatalogId, "Main");
        foreach (var line in envelope.Frames!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            surface.Components.Add(A2UiFrameItem.FromJson(surfaceId, doc.RootElement, SendA2UiEventAsync));
        }

        ActiveCanvasSurface = surface;
        IsCanvasVisible = true;
        CanvasStatus = $"Rendered {validation.FrameCount} A2UI frame(s).";
        NotifySurfaceStateChanged();
        await SendCanvasAckAsync(envelope, success: true, error: null);
    }

    private async Task ApplyA2UiCreateSurfaceAsync(WsServerEnvelope envelope, string surfaceId)
    {
        var components = BuildV09Components(surfaceId, envelope.Components, out var diagnostics);
        var surface = GetOrCreateSurface(surfaceId, envelope.CatalogId, envelope.SurfaceTitle);
        surface.CatalogId = string.IsNullOrWhiteSpace(envelope.CatalogId) ? A2UiCatalogRegistry.AGenUiCatalogId : envelope.CatalogId!;
        surface.Title = string.IsNullOrWhiteSpace(envelope.SurfaceTitle) ? surfaceId : envelope.SurfaceTitle!;
        surface.DataModelJson = envelope.DataModelJson;
        surface.Components.Clear();
        surface.Diagnostics.Clear();
        foreach (var item in components)
            surface.Components.Add(item);
        foreach (var diagnostic in diagnostics)
            surface.Diagnostics.Add(diagnostic);
        ActiveCanvasSurface = surface;
        IsCanvasVisible = true;
        CanvasStatus = $"Created A2UI surface '{surface.Title}'.";
        NotifySurfaceStateChanged();
        await SendCanvasAckAsync(envelope, success: true, error: null);
    }

    private async Task ApplyA2UiUpdateComponentsAsync(WsServerEnvelope envelope, string surfaceId)
    {
        var components = BuildV09Components(surfaceId, envelope.Components, out var diagnostics);
        var surface = GetOrCreateSurface(surfaceId, envelope.CatalogId, envelope.SurfaceTitle);
        surface.Components.Clear();
        surface.Diagnostics.Clear();
        foreach (var item in components)
            surface.Components.Add(item);
        foreach (var diagnostic in diagnostics)
            surface.Diagnostics.Add(diagnostic);
        ActiveCanvasSurface = surface;
        IsCanvasVisible = true;
        CanvasStatus = $"Updated A2UI surface '{surface.Title}'.";
        NotifySurfaceStateChanged();
        await SendCanvasAckAsync(envelope, success: true, error: null);
    }

    private async Task ApplyA2UiUpdateDataModelAsync(WsServerEnvelope envelope, string surfaceId)
    {
        var surface = GetOrCreateSurface(surfaceId, envelope.CatalogId, envelope.SurfaceTitle);
        surface.DataModelJson = envelope.DataModelJson;
        ActiveCanvasSurface = surface;
        IsCanvasVisible = true;
        CanvasStatus = $"Updated A2UI data model for '{surface.Title}'.";
        NotifySurfaceStateChanged();
        await SendCanvasAckAsync(envelope, success: true, error: null);
    }

    private async Task ApplyA2UiDeleteSurfaceAsync(WsServerEnvelope envelope, string surfaceId)
    {
        var surface = CanvasSurfaces.FirstOrDefault(s => string.Equals(s.SurfaceId, surfaceId, StringComparison.OrdinalIgnoreCase));
        if (surface is not null)
            CanvasSurfaces.Remove(surface);

        if (ReferenceEquals(ActiveCanvasSurface, surface))
            ActiveCanvasSurface = CanvasSurfaces.FirstOrDefault();
        else
            SyncCompatibilityCanvasFrames();

        IsCanvasVisible = CanvasSurfaces.Count > 0;
        CanvasStatus = $"Deleted A2UI surface '{surfaceId}'.";
        NotifySurfaceStateChanged();
        await SendCanvasAckAsync(envelope, success: true, error: null);
    }

    private A2UiSurfaceItem GetOrCreateSurface(string surfaceId, string? catalogId, string? title)
    {
        var surface = CanvasSurfaces.FirstOrDefault(s => string.Equals(s.SurfaceId, surfaceId, StringComparison.OrdinalIgnoreCase));
        if (surface is not null)
            return surface;

        surface = new A2UiSurfaceItem(
            surfaceId,
            string.IsNullOrWhiteSpace(catalogId) ? A2UiCatalogRegistry.AGenUiCatalogId : catalogId!,
            string.IsNullOrWhiteSpace(title) ? surfaceId : title!);
        CanvasSurfaces.Add(surface);
        OnPropertyChanged(nameof(HasCanvasSurfaces));
        return surface;
    }

    private void AddV09Components(A2UiSurfaceItem surface, string[]? components)
    {
        if (components is null)
            return;

        var items = BuildV09Components(surface.SurfaceId, components, out var diagnostics);
        foreach (var item in items)
            surface.Components.Add(item);
        foreach (var diagnostic in diagnostics)
            surface.Diagnostics.Add(diagnostic);
    }

    private IReadOnlyList<A2UiFrameItem> BuildV09Components(string surfaceId, string[]? components, out IReadOnlyList<string> diagnostics)
    {
        var items = new List<A2UiFrameItem>();
        var diagnosticItems = new List<string>();
        if (components is not null)
        {
            foreach (var componentJson in components)
            {
                using var doc = JsonDocument.Parse(componentJson);
                var item = A2UiFrameItem.FromJson(surfaceId, doc.RootElement, SendA2UiActionAsync);
                if (item.IsUnsupportedFallback)
                    diagnosticItems.Add($"Unsupported AGenUI component '{item.Type}' rendered as fallback.");
                items.Add(item);
            }
        }

        diagnostics = diagnosticItems;
        return items;
    }

    private void SyncCompatibilityCanvasFrames()
    {
        CanvasFrames.Clear();
        if (ActiveCanvasSurface is not null)
        {
            foreach (var component in ActiveCanvasSurface.Components)
                CanvasFrames.Add(component);
        }
        OnPropertyChanged(nameof(HasCanvasFrames));
    }

    private void NotifySurfaceStateChanged()
    {
        SyncCompatibilityCanvasFrames();
        OnPropertyChanged(nameof(HasCanvasFrames));
        OnPropertyChanged(nameof(HasCanvasSurfaces));
        OnPropertyChanged(nameof(HasActiveCanvasSurfaceDiagnostics));
    }

    private async Task SendCanvasAckAsync(WsServerEnvelope envelope, bool success, string? error)
    {
        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "canvas_ack",
            RequestId = envelope.RequestId,
            SessionId = _canvasSessionId,
            SurfaceId = envelope.SurfaceId,
            Success = success,
            Error = error
        }, CancellationToken.None);
    }

    private async Task SendCanvasEvalResultAsync(WsServerEnvelope envelope, bool success, string? valueJson, string? error)
    {
        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "canvas_eval_result",
            RequestId = envelope.RequestId,
            SessionId = _canvasSessionId,
            SurfaceId = envelope.SurfaceId,
            Success = success,
            ValueJson = valueJson,
            Error = error
        }, CancellationToken.None);
    }

    private async Task SendA2UiSyncResultAsync(WsServerEnvelope envelope)
    {
        var dataModelJson = BuildA2UiSyncDataModelJson(envelope.SurfaceId);
        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "a2ui_sync_result",
            RequestId = envelope.RequestId,
            SessionId = _canvasSessionId,
            SurfaceId = envelope.SurfaceId,
            ComponentId = envelope.ComponentId,
            SyncMode = envelope.SyncMode,
            Success = true,
            ValueJson = dataModelJson
        }, CancellationToken.None);
    }

    private async Task SendCanvasSnapshotResultAsync(WsServerEnvelope envelope)
    {
        var snapshot = BuildCanvasSnapshotJson(envelope.SurfaceId);
        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "canvas_snapshot_result",
            RequestId = envelope.RequestId,
            SessionId = _canvasSessionId,
            SurfaceId = envelope.SurfaceId,
            Success = true,
            SnapshotMode = envelope.SnapshotMode ?? "state",
            SnapshotJson = snapshot
        }, CancellationToken.None);
    }

    private async Task SendA2UiEventAsync(A2UiFrameItem frame, string eventName, string valueJson)
    {
        if (!_client.IsConnected)
            return;

        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "a2ui_event",
            SessionId = _canvasSessionId,
            SurfaceId = frame.SurfaceId,
            ComponentId = frame.Id,
            Event = eventName,
            ValueJson = valueJson,
            Sequence = Interlocked.Increment(ref _canvasEventSequence)
        }, CancellationToken.None);
    }

    private async Task SendA2UiActionAsync(A2UiFrameItem frame, string actionName, string parametersJson)
    {
        if (!_client.IsConnected)
            return;

        await _client.SendEnvelopeAsync(new WsClientEnvelope
        {
            Type = "a2ui_action",
            Operation = "action",
            SessionId = _canvasSessionId,
            SurfaceId = frame.SurfaceId,
            ComponentId = frame.Id,
            Action = actionName,
            ValueJson = parametersJson,
            Sequence = Interlocked.Increment(ref _canvasEventSequence)
        }, CancellationToken.None);
    }

    private string BuildA2UiSyncDataModelJson(string? surfaceId)
    {
        var surface = FindCanvasSurface(surfaceId);
        if (IsJsonObject(surface?.DataModelJson))
            return surface!.DataModelJson!;

        var values = (surface?.Components ?? CanvasFrames)
            .Where(static frame => !string.IsNullOrWhiteSpace(frame.ValueText) || !string.IsNullOrWhiteSpace(frame.SelectedValue))
            .ToDictionary(static frame => frame.Id, static frame => (object?)(frame.SelectedValue ?? frame.ValueText));
        return JsonSerializer.Serialize(values);
    }

    private string BuildCanvasSnapshotJson(string? surfaceId)
    {
        var resolvedSurfaceId = ResolveSurfaceId(surfaceId);
        var surface = FindCanvasSurface(resolvedSurfaceId);
        var sourceFrames = surface?.Components ?? CanvasFrames;
        var totalFrames = sourceFrames.Count;
        var frames = sourceFrames.Take(MaxSnapshotFrames).Select(frame => new
        {
            frame.SurfaceId,
            frame.Id,
            frame.Type,
            Title = TruncateSnapshotText(frame.Title),
            Text = TruncateSnapshotText(frame.Text),
            Label = TruncateSnapshotText(frame.Label),
            ValueText = TruncateSnapshotText(frame.ValueText),
            SelectedValue = TruncateSnapshotText(frame.SelectedValue)
        }).ToArray();

        var diagnostics = new List<string>();
        if (!string.IsNullOrWhiteSpace(CanvasHtmlStatus))
            diagnostics.Add(CanvasHtmlStatus);
        if (surface is not null)
            diagnostics.AddRange(surface.Diagnostics);

        var dataModel = ParseJsonObjectOrNull(surface?.DataModelJson, diagnostics);
        var snapshot = JsonSerializer.Serialize(new
        {
            type = "canvas_snapshot",
            surfaceId = resolvedSurfaceId,
            visible = IsCanvasVisible,
            frameCount = totalFrames,
            returnedFrameCount = frames.Length,
            truncated = totalFrames > frames.Length,
            dataModel,
            frames,
            diagnostics = diagnostics.ToArray()
        });

        if (snapshot.Length <= MaxSnapshotJsonChars)
            return snapshot;

        return JsonSerializer.Serialize(new
        {
            type = "canvas_snapshot",
            surfaceId = resolvedSurfaceId,
            visible = IsCanvasVisible,
            frameCount = totalFrames,
            returnedFrameCount = 0,
            truncated = true,
            frames = Array.Empty<object>(),
            diagnostics = new[] { $"Snapshot exceeded {MaxSnapshotJsonChars} characters and was truncated." }
        });
    }

    private A2UiSurfaceItem? FindCanvasSurface(string? surfaceId)
    {
        var resolvedSurfaceId = ResolveSurfaceId(surfaceId);
        return CanvasSurfaces.FirstOrDefault(s => string.Equals(s.SurfaceId, resolvedSurfaceId, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveSurfaceId(string? surfaceId)
        => string.IsNullOrWhiteSpace(surfaceId) ? ActiveCanvasSurface?.SurfaceId ?? "main" : surfaceId!;

    private static JsonElement? ParseJsonObjectOrNull(string? json, ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            diagnostics.Add("Surface dataModelJson is not valid JSON and was omitted from the snapshot.");
            return null;
        }
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TruncateSnapshotText(string? value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxSnapshotTextChars
            ? value
            : string.Concat(value.AsSpan(0, MaxSnapshotTextChars), "...");

    [RelayCommand]
    private void ClearCanvas()
    {
        CanvasSurfaces.Clear();
        ActiveCanvasSurface = null;
        CanvasFrames.Clear();
        CanvasHtmlStatus = "";
        CanvasStatus = "Canvas cleared.";
        OnPropertyChanged(nameof(HasCanvasFrames));
        OnPropertyChanged(nameof(HasCanvasSurfaces));
    }

    private static bool IsCanvasServerEnvelope(string type)
        => type is "canvas_present" or "canvas_hide" or "canvas_navigate" or "canvas_snapshot" or "a2ui_push" or "a2ui_reset" or "a2ui_eval" or "a2ui_create_surface" or "a2ui_update_components" or "a2ui_update_data_model" or "a2ui_delete_surface" or "a2ui_sync_ui_to_data";
}
