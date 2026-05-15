using System.Text;
using System.Text.Json;
using OpenClaw.Core.Abstractions;
using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;

namespace OpenClaw.Gateway.Tools;

internal abstract class CanvasToolBase : IToolWithContext
{
    protected CanvasToolBase(CanvasCommandBroker broker, GatewayConfig config)
    {
        Broker = broker;
        Config = config;
    }

    protected CanvasCommandBroker Broker { get; }
    protected GatewayConfig Config { get; }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string ParameterSchema { get; }

    public ValueTask<string> ExecuteAsync(string argumentsJson, CancellationToken ct)
        => new("Error: Canvas tools require session context.");

    public abstract ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct);

    protected async ValueTask<string> SendAsync(
        string argumentsJson,
        ToolExecutionContext context,
        WsServerEnvelope command,
        string expectedResponseType,
        string? requiredCapability,
        CancellationToken ct)
    {
        var result = await Broker.SendCommandAsync(
            context.Session,
            command,
            expectedResponseType,
            requiredCapability,
            ct);

        if (!result.Success)
            return $"Error: {result.Error}";

        if (!string.IsNullOrWhiteSpace(result.SnapshotJson))
            return result.SnapshotJson!;
        if (!string.IsNullOrWhiteSpace(result.ValueJson))
            return result.ValueJson!;

        return $"Canvas command accepted. requestId={result.RequestId}";
    }

    protected static JsonDocument ParseArgs(string argumentsJson)
        => JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

    protected static string SurfaceId(JsonElement root)
        => TryGetString(root, "surfaceId") ?? "main";

    protected static string? TryGetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    protected static bool TryGetRequiredString(JsonElement root, string propertyName, out string value, out string error)
    {
        if (TryGetString(root, propertyName) is { Length: > 0 } found)
        {
            value = found;
            error = "";
            return true;
        }

        value = "";
        error = $"Error: '{propertyName}' is required.";
        return false;
    }

    protected static bool TryGetOptionalStringArray(JsonElement root, string propertyName, out string[]? values, out string error)
    {
        values = null;
        error = "";
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return true;

        if (prop.ValueKind != JsonValueKind.Array)
        {
            error = $"Error: '{propertyName}' must be a JSON string array.";
            return false;
        }

        var items = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"Error: '{propertyName}' must be a JSON string array.";
                return false;
            }

            items.Add(item.GetString() ?? "");
        }

        values = items.ToArray();
        return true;
    }

    protected static bool TryGetRequiredStringArray(JsonElement root, string propertyName, out string[] values, out string error)
    {
        values = [];
        if (!TryGetOptionalStringArray(root, propertyName, out var found, out error))
            return false;

        if (found is { Length: > 0 })
        {
            values = found;
            return true;
        }

        error = $"Error: '{propertyName}' is required as a non-empty JSON string array.";
        return false;
    }

    protected static string? ValidateOptionalJsonObject(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : $"Error: '{propertyName}' must be a JSON object.";
        }
        catch (JsonException ex)
        {
            return $"Error: '{propertyName}' is not valid JSON: {ex.Message}";
        }
    }

    protected string? ValidateEnvelopePayloadSize(WsServerEnvelope envelope)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, CoreJsonContext.Default.WsServerEnvelope);
        var maxBytes = MaxPayloadBytesWithEnvelopeReserve();
        return bytes.Length <= maxBytes
            ? null
            : $"Error: payload exceeds {maxBytes} bytes.";
    }

    protected static string? ValidateA2UiV09Envelope(WsServerEnvelope envelope)
    {
        var validation = A2UiV09MessageValidator.Validate(envelope);
        return validation.IsValid ? null : $"Error: {validation.Error}";
    }

    protected bool TryResolveCatalog(string senderId, string? requestedCatalogId, out A2UiCatalogDescriptor catalog, out string error)
    {
        if (Broker.TryChooseCatalog(senderId, requestedCatalogId, out var chosen, out var brokerError) && chosen is not null)
        {
            catalog = chosen;
            error = "";
            return true;
        }

        catalog = null!;
        error = $"Error: {brokerError ?? "Canvas client does not support the requested catalog."}";
        return false;
    }

    protected bool TryGetLockedOrResolveCatalog(ToolExecutionContext context, string surfaceId, out A2UiCatalogDescriptor catalog, out string error)
    {
        var requestedCatalogId = Broker.TryGetSurfaceCatalogId(context.Session.SenderId, context.Session.Id, surfaceId, out var lockedCatalogId)
            ? lockedCatalogId
            : null;
        return TryResolveCatalog(context.Session.SenderId, requestedCatalogId, out catalog, out error);
    }

    protected int MaxPayloadBytesWithEnvelopeReserve()
        => Math.Max(1, Config.Canvas.MaxCommandBytes - 4096);
}

internal sealed class CanvasPresentTool : CanvasToolBase
{
    public CanvasPresentTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "canvas_present";
    public override string Description => "Show the current session's Canvas visual workspace.";
    public override string ParameterSchema => """{"type":"object","properties":{"surfaceId":{"type":"string","default":"main"}}}""";

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "canvas_present",
            SurfaceId = SurfaceId(args.RootElement)
        }, "canvas_ack", "canvas.present", ct);
    }
}

internal sealed class CanvasHideTool : CanvasToolBase
{
    public CanvasHideTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "canvas_hide";
    public override string Description => "Hide the current session's Canvas visual workspace.";
    public override string ParameterSchema => """{"type":"object","properties":{"surfaceId":{"type":"string","default":"main"}}}""";

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "canvas_hide",
            SurfaceId = SurfaceId(args.RootElement)
        }, "canvas_ack", "canvas.hide", ct);
    }
}

internal sealed class CanvasNavigateTool : CanvasToolBase
{
    public CanvasNavigateTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "canvas_navigate";
    public override string Description => "Load local Canvas HTML, about:blank, or an openclaw-canvas:// artifact into the session Canvas. Remote HTTP/HTTPS pages are not supported by Canvas v1.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string","default":"main"},"html":{"type":"string"},"url":{"type":"string"},"contentType":{"type":"string","default":"text/html"}}}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        var root = args.RootElement;
        var html = TryGetString(root, "html");
        var url = TryGetString(root, "url");
        if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(url))
            return "Error: 'html' or 'url' is required.";
        if (!string.IsNullOrWhiteSpace(html) && !Config.Canvas.EnableLocalHtml)
            return "Error: local Canvas HTML is disabled.";
        if (!string.IsNullOrWhiteSpace(url) && !IsAllowedCanvasUrl(url!))
            return "Error: Canvas v1 only supports about:blank and openclaw-canvas:// URLs; use the browser tool for remote webpages.";

        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "canvas_navigate",
            SurfaceId = SurfaceId(root),
            Html = html,
            Url = url,
            ContentType = TryGetString(root, "contentType") ?? "text/html"
        }, "canvas_ack", string.IsNullOrWhiteSpace(html) ? "canvas.present" : "canvas.local_html", ct);
    }

    private static bool IsAllowedCanvasUrl(string url)
        => string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase) ||
           url.StartsWith("openclaw-canvas://", StringComparison.OrdinalIgnoreCase);
}

internal sealed class CanvasSnapshotTool : CanvasToolBase
{
    public CanvasSnapshotTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "canvas_snapshot";
    public override string Description => "Capture a lightweight JSON state snapshot of the current session Canvas.";
    public override string ParameterSchema => """{"type":"object","properties":{"surfaceId":{"type":"string","default":"main"},"snapshotMode":{"type":"string","default":"state"}}}""";

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "canvas_snapshot",
            SurfaceId = SurfaceId(args.RootElement),
            SnapshotMode = TryGetString(args.RootElement, "snapshotMode") ?? "state"
        }, "canvas_snapshot_result", "snapshot.state", ct);
    }
}

internal sealed class A2UiPushTool : CanvasToolBase
{
    public A2UiPushTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_push";
    public override string Description => "Push A2UI v0.8 JSONL frames to the current session Canvas.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string","default":"main"},"frames":{"type":"string","description":"A2UI v0.8 JSONL frames"}},"required":["frames"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        if (!TryGetRequiredString(args.RootElement, "frames", out var frames, out var error))
            return error;

        var validation = A2UiFrameValidator.ValidateJsonl(frames, Config.Canvas.MaxFramesPerPush, MaxPayloadBytesWithEnvelopeReserve());
        if (!validation.IsValid)
            return $"Error: {validation.Error}";

        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "a2ui_push",
            SurfaceId = SurfaceId(args.RootElement),
            ContentType = A2UiFrameValidator.ContentTypeV08,
            Frames = frames
        }, "canvas_ack", "a2ui.v0_8", ct);
    }
}

internal sealed class A2UiResetTool : CanvasToolBase
{
    public A2UiResetTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_reset";
    public override string Description => "Clear A2UI-rendered content from the current session Canvas.";
    public override string ParameterSchema => """{"type":"object","properties":{"surfaceId":{"type":"string","default":"main"}}}""";

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "a2ui_reset",
            SurfaceId = SurfaceId(args.RootElement)
        }, "canvas_ack", "a2ui.v0_8", ct);
    }
}

internal sealed class A2UiEvalTool : CanvasToolBase
{
    public A2UiEvalTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_eval";
    public override string Description => "Run JavaScript in the local A2UI Canvas sandbox. This does not evaluate scripts on remote webpages.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string","default":"main"},"script":{"type":"string"}},"required":["script"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        if (!Config.Canvas.EnableEval)
            return "Error: A2UI eval is disabled.";

        using var args = ParseArgs(argumentsJson);
        if (!TryGetRequiredString(args.RootElement, "script", out var script, out var error))
            return error;

        if (Encoding.UTF8.GetByteCount(script) > Math.Max(1, Config.Canvas.MaxCommandBytes))
            return $"Error: script exceeds {Config.Canvas.MaxCommandBytes} bytes.";

        return await SendAsync(argumentsJson, context, new WsServerEnvelope
        {
            Type = "a2ui_eval",
            SurfaceId = SurfaceId(args.RootElement),
            Script = script
        }, "canvas_eval_result", "a2ui.eval", ct);
    }
}

internal sealed class A2UiCreateSurfaceTool : CanvasToolBase
{
    public A2UiCreateSurfaceTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_create_surface";
    public override string Description => "Create an A2UI v0.9 surface in the current session Canvas.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string"},"catalogId":{"type":"string"},"title":{"type":"string"},"metadata":{"type":"string","description":"JSON object metadata"},"components":{"type":"array","items":{"type":"string"}},"dataModelJson":{"type":"string"}},"required":["surfaceId"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        var root = args.RootElement;
        if (!TryGetRequiredString(root, "surfaceId", out var surfaceId, out var error)) return error;
        var catalogId = TryGetString(root, "catalogId");
        var title = TryGetString(root, "title");
        var metadata = TryGetString(root, "metadata");
        if ((error = ValidateOptionalJsonObject(metadata, "metadata")) is not null) return error;
        if (!TryGetOptionalStringArray(root, "components", out var components, out error)) return error;
        var dataModelJson = TryGetString(root, "dataModelJson");

        if (!TryResolveCatalog(context.Session.SenderId, catalogId, out var catalog, out error)) return error;

        var envelope = new WsServerEnvelope
        {
            Type = "a2ui_create_surface",
            Operation = "createSurface",
            SurfaceId = surfaceId,
            CatalogId = catalog.CatalogId,
            SurfaceTitle = title,
            ParametersJson = metadata,
            Components = components,
            DataModelJson = dataModelJson
        };

        if ((error = ValidateA2UiV09Envelope(envelope)) is not null) return error;
        if ((error = ValidateEnvelopePayloadSize(envelope)) is not null) return error;
        return await SendAsync(argumentsJson, context, envelope, "canvas_ack", "a2ui.v0_9", ct);
    }
}

internal sealed class A2UiUpdateComponentsTool : CanvasToolBase
{
    public A2UiUpdateComponentsTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_update_components";
    public override string Description => "Update A2UI v0.9 surface components using a JSON string array.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string"},"components":{"type":"array","items":{"type":"string"}}},"required":["surfaceId","components"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        if (!TryGetRequiredString(args.RootElement, "surfaceId", out var surfaceId, out var error)) return error;
        if (!TryGetRequiredStringArray(args.RootElement, "components", out var components, out error)) return error;

        if (!TryGetLockedOrResolveCatalog(context, surfaceId, out var catalog, out error)) return error;

        var envelope = new WsServerEnvelope
        {
            Type = "a2ui_update_components",
            Operation = "updateComponents",
            SurfaceId = surfaceId,
            CatalogId = catalog.CatalogId,
            Components = components
        };

        if ((error = ValidateA2UiV09Envelope(envelope)) is not null) return error;
        if ((error = ValidateEnvelopePayloadSize(envelope)) is not null) return error;
        return await SendAsync(argumentsJson, context, envelope, "canvas_ack", "a2ui.v0_9", ct);
    }
}

internal sealed class A2UiUpdateDataModelTool : CanvasToolBase
{
    public A2UiUpdateDataModelTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_update_data_model";
    public override string Description => "Update an A2UI v0.9 surface data model.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string"},"dataModelJson":{"type":"string"}},"required":["surfaceId","dataModelJson"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        if (!TryGetRequiredString(args.RootElement, "surfaceId", out var surfaceId, out var error)) return error;
        if (!TryGetRequiredString(args.RootElement, "dataModelJson", out var dataModelJson, out error)) return error;

        if (!TryGetLockedOrResolveCatalog(context, surfaceId, out var catalog, out error)) return error;

        var envelope = new WsServerEnvelope
        {
            Type = "a2ui_update_data_model",
            Operation = "updateDataModel",
            SurfaceId = surfaceId,
            CatalogId = catalog.CatalogId,
            DataModelJson = dataModelJson
        };

        if ((error = ValidateA2UiV09Envelope(envelope)) is not null) return error;
        if ((error = ValidateEnvelopePayloadSize(envelope)) is not null) return error;
        return await SendAsync(argumentsJson, context, envelope, "canvas_ack", "a2ui.v0_9", ct);
    }
}

internal sealed class A2UiDeleteSurfaceTool : CanvasToolBase
{
    public A2UiDeleteSurfaceTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_delete_surface";
    public override string Description => "Delete an A2UI v0.9 surface.";
    public override string ParameterSchema => """{"type":"object","properties":{"surfaceId":{"type":"string"}},"required":["surfaceId"]}""";

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        if (!TryGetRequiredString(args.RootElement, "surfaceId", out var surfaceId, out var error)) return error;
        var envelope = new WsServerEnvelope
        {
            Type = "a2ui_delete_surface",
            Operation = "deleteSurface",
            SurfaceId = surfaceId
        };

        if ((error = ValidateA2UiV09Envelope(envelope)) is not null) return error;
        if ((error = ValidateEnvelopePayloadSize(envelope)) is not null) return error;
        return await SendAsync(argumentsJson, context, envelope, "canvas_ack", "a2ui.v0_9", ct);
    }
}

internal sealed class A2UiSyncUiToDataTool : CanvasToolBase
{
    public A2UiSyncUiToDataTool(CanvasCommandBroker broker, GatewayConfig config) : base(broker, config) { }
    public override string Name => "a2ui_sync_ui_to_data";
    public override string Description => "Synchronize A2UI v0.9 UI state into a data model.";
    public override string ParameterSchema => """
        {"type":"object","properties":{"surfaceId":{"type":"string"},"componentId":{"type":"string"},"syncMode":{"type":"string"}},"required":["surfaceId"]}
        """;

    public override async ValueTask<string> ExecuteAsync(string argumentsJson, ToolExecutionContext context, CancellationToken ct)
    {
        using var args = ParseArgs(argumentsJson);
        var root = args.RootElement;
        if (!TryGetRequiredString(root, "surfaceId", out var surfaceId, out var error)) return error;
        var envelope = new WsServerEnvelope
        {
            Type = "a2ui_sync_ui_to_data",
            Operation = "syncUIToData",
            SurfaceId = surfaceId,
            ComponentId = TryGetString(root, "componentId"),
            SyncMode = TryGetString(root, "syncMode")
        };

        if ((error = ValidateA2UiV09Envelope(envelope)) is not null) return error;
        if ((error = ValidateEnvelopePayloadSize(envelope)) is not null) return error;
        return await SendAsync(argumentsJson, context, envelope, "a2ui_sync_result", "a2ui.v0_9", ct);
    }
}
