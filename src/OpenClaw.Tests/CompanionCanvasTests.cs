using System.Reflection;
using System.Text;
using System.Text.Json;
using OpenClaw.Companion.Services;
using OpenClaw.Companion.ViewModels;
using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;
using Xunit;

namespace OpenClaw.Tests;

public sealed class CompanionCanvasTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task SendCanvasReady_AdvertisesV09AndSupportedCatalogs()
    {
        var (viewModel, ws) = CreateViewModel();

        await SendCanvasReadyAsync(viewModel);

        var ready = LastSentEnvelope(ws);
        Assert.Equal("canvas_ready", ready.Type);
        Assert.Contains("a2ui.v0_9", ready.Capabilities ?? []);
        Assert.Contains(A2UiCatalogRegistry.OpenClawV08CatalogId, ready.SupportedCatalogIds ?? []);
        Assert.Contains(A2UiCatalogRegistry.AGenUiCatalogId, ready.SupportedCatalogIds ?? []);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_PushRendersNativeFramesAndAcks()
    {
        var (viewModel, ws) = CreateViewModel();

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_push",
            RequestId = "req1",
            SessionId = "sess",
            SurfaceId = "main",
            Frames = """
                {"type":"button","id":"save","label":"Save"}
                {"type":"input","id":"name","label":"Name","value":"Ada"}
                """
        });

        Assert.True(viewModel.IsCanvasVisible);
        Assert.True(viewModel.HasCanvasFrames);
        Assert.Equal(2, viewModel.CanvasFrames.Count);
        Assert.Equal("Ada", viewModel.CanvasFrames.Single(frame => frame.Id == "name").ValueText);

        var ack = LastSentEnvelope(ws);
        Assert.Equal("canvas_ack", ack.Type);
        Assert.Equal("req1", ack.RequestId);
        Assert.True(ack.Success);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_ButtonEventSendsA2UiEvent()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_push",
            RequestId = "req1",
            SessionId = "sess",
            SurfaceId = "main",
            Frames = """{"type":"button","id":"save","label":"Save"}"""
        });

        await viewModel.CanvasFrames.Single().ActivateCommand.ExecuteAsync(null);

        var evt = LastSentEnvelope(ws);
        Assert.Equal("a2ui_event", evt.Type);
        Assert.Equal("sess", evt.SessionId);
        Assert.Equal("save", evt.ComponentId);
        Assert.Equal("click", evt.Event);
        Assert.Equal("true", evt.ValueJson);
        Assert.Equal(1, evt.Sequence);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_SnapshotReturnsStateJson()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_push",
            RequestId = "req1",
            SessionId = "sess",
            SurfaceId = "main",
            Frames = """{"type":"text","id":"summary","text":"Ready"}"""
        });

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "canvas_snapshot",
            RequestId = "snap1",
            SessionId = "sess",
            SurfaceId = "main"
        });

        var snapshot = LastSentEnvelope(ws);
        Assert.Equal("canvas_snapshot_result", snapshot.Type);
        Assert.Equal("snap1", snapshot.RequestId);
        Assert.NotNull(snapshot.SnapshotJson);

        using var doc = JsonDocument.Parse(snapshot.SnapshotJson!);
        Assert.Equal(1, doc.RootElement.GetProperty("frameCount").GetInt32());
        Assert.True(doc.RootElement.GetProperty("visible").GetBoolean());
    }

    [Fact]
    public void A2UiFrameItem_ClampsNegativeProgressToZero()
    {
        using var doc = JsonDocument.Parse("""{"type":"progress","id":"p","value":-0.25}""");

        var item = A2UiFrameItem.FromJson("main", doc.RootElement, (_, _, _) => Task.CompletedTask);

        Assert.Equal(0, item.ProgressValue);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_SnapshotBoundsReturnedFrames()
    {
        var (viewModel, ws) = CreateViewModel();
        var frames = string.Join('\n', Enumerable.Range(0, 105)
            .Select(i => $$"""{"type":"text","id":"f{{i}}","text":"{{new string('x', 2000)}}"}"""));
        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_push",
            RequestId = "req1",
            SessionId = "sess",
            SurfaceId = "main",
            Frames = frames
        });

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "canvas_snapshot",
            RequestId = "snap1",
            SessionId = "sess",
            SurfaceId = "main"
        });

        var snapshot = LastSentEnvelope(ws);
        using var doc = JsonDocument.Parse(snapshot.SnapshotJson!);

        Assert.Equal(105, doc.RootElement.GetProperty("frameCount").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(snapshot.SnapshotJson!.Length <= 128 * 1024);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09CreateUpdateDeleteMaintainsIndependentSurfaces()
    {
        var (viewModel, ws) = CreateViewModel();

        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha 1")]));
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("beta", "Beta", [TextComponent("beta-text", "Beta 1")]));
        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_update_components",
            Operation = "updateComponents",
            RequestId = "update-alpha",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = [TextComponent("alpha-text", "Alpha 2")]
        });

        Assert.Equal(2, viewModel.CanvasSurfaces.Count);
        Assert.Equal("alpha", viewModel.ActiveCanvasSurface?.SurfaceId);
        Assert.Equal("Alpha 2", viewModel.CanvasSurfaces.Single(surface => surface.SurfaceId == "alpha").Components.Single().Text);
        Assert.Equal("Beta 1", viewModel.CanvasSurfaces.Single(surface => surface.SurfaceId == "beta").Components.Single().Text);
        Assert.Equal("alpha-text", viewModel.CanvasFrames.Single().Id);

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_delete_surface",
            Operation = "deleteSurface",
            RequestId = "delete-alpha",
            SessionId = "sess",
            SurfaceId = "alpha"
        });

        Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("beta", viewModel.ActiveCanvasSurface?.SurfaceId);
        Assert.Equal("beta-text", viewModel.CanvasFrames.Single().Id);
        Assert.Equal("delete-alpha", LastSentEnvelope(ws).RequestId);
        Assert.True(LastSentEnvelope(ws).Success);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09SnapshotIsSurfaceSpecific()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")], "{\"alpha\":1}"));
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("beta", "Beta", [TextComponent("beta-text", "Beta")], "{\"beta\":2}"));

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "canvas_snapshot",
            RequestId = "snap-alpha",
            SessionId = "sess",
            SurfaceId = "alpha"
        });

        var snapshot = LastSentEnvelope(ws);
        using var doc = JsonDocument.Parse(snapshot.SnapshotJson!);
        Assert.Equal("alpha", doc.RootElement.GetProperty("surfaceId").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("frameCount").GetInt32());
        Assert.Equal("alpha-text", doc.RootElement.GetProperty("frames")[0].GetProperty("Id").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("dataModel").GetProperty("alpha").GetInt32());
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09DataModelUpdateKeepsExistingComponents()
    {
        var (viewModel, _) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")], "{\"count\":1}"));

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_update_data_model",
            Operation = "updateDataModel",
            RequestId = "data-alpha",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            DataModelJson = "{\"count\":2}"
        });

        var surface = Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("alpha-text", surface.Components.Single().Id);
        Assert.Equal("{\"count\":2}", surface.DataModelJson);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09ComponentActionSendsA2UiAction()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [ButtonComponent("save", "Save")]));

        await viewModel.ActiveCanvasSurface!.Components.Single().ActivateCommand.ExecuteAsync(null);

        var action = LastSentEnvelope(ws);
        Assert.Equal("a2ui_action", action.Type);
        Assert.Equal("sess", action.SessionId);
        Assert.Equal("alpha", action.SurfaceId);
        Assert.Equal("save", action.ComponentId);
        Assert.Equal("click", action.Action);
        Assert.Equal("true", action.ValueJson);
        Assert.Equal(1, action.Sequence);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09SyncUiToDataReturnsDataModelValueJson()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")], "{\"count\":2}"));

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_sync_ui_to_data",
            Operation = "syncUIToData",
            RequestId = "sync-alpha",
            SessionId = "sess",
            SurfaceId = "alpha",
            SyncMode = "pull"
        });

        var result = LastSentEnvelope(ws);
        Assert.Equal("a2ui_sync_result", result.Type);
        Assert.Equal("sync-alpha", result.RequestId);
        Assert.Equal("alpha", result.SurfaceId);
        Assert.Equal("pull", result.SyncMode);
        Assert.Equal("{\"count\":2}", result.ValueJson);
        Assert.Null(result.DataModelJson);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09MalformedCreateKeepsPreviousSurfaceState()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")], "{\"count\":1}"));

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_create_surface",
            Operation = "createSurface",
            RequestId = "bad-create",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            SurfaceTitle = "Changed",
            DataModelJson = "{\"count\":2}",
            Components = [TextComponent("replacement", "Replacement"), "not-json"]
        });

        var surface = Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("Alpha", surface.Title);
        Assert.Equal("alpha-text", surface.Components.Single().Id);
        Assert.Equal("{\"count\":1}", surface.DataModelJson);

        var ack = LastSentEnvelope(ws);
        Assert.Equal("bad-create", ack.RequestId);
        Assert.False(ack.Success);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09MalformedComponentUpdateKeepsPreviousSurfaceState()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")], "{\"count\":1}"));

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_update_components",
            Operation = "updateComponents",
            RequestId = "bad-update",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = [TextComponent("replacement", "Replacement"), "not-json"]
        });

        var surface = Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("alpha-text", surface.Components.Single().Id);
        Assert.Equal("{\"count\":1}", surface.DataModelJson);

        var ack = LastSentEnvelope(ws);
        Assert.Equal("bad-update", ack.RequestId);
        Assert.False(ack.Success);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09MalformedComponentUpdateDoesNotCreateSurface()
    {
        var (viewModel, ws) = CreateViewModel();

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_update_components",
            Operation = "updateComponents",
            RequestId = "bad-update",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = ["not-json"]
        });

        Assert.Empty(viewModel.CanvasSurfaces);
        Assert.Null(viewModel.ActiveCanvasSurface);

        var ack = LastSentEnvelope(ws);
        Assert.Equal("bad-update", ack.RequestId);
        Assert.False(ack.Success);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09SnapshotWithMalformedDataModelReturnsDiagnosticSnapshot()
    {
        var (viewModel, ws) = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha")]));
        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_update_data_model",
            Operation = "updateDataModel",
            RequestId = "bad-data",
            SessionId = "sess",
            SurfaceId = "alpha",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            DataModelJson = "not-json"
        });

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "canvas_snapshot",
            RequestId = "snap-alpha",
            SessionId = "sess",
            SurfaceId = "alpha"
        });

        var snapshot = LastSentEnvelope(ws);
        Assert.Equal("canvas_snapshot_result", snapshot.Type);
        Assert.True(snapshot.Success);
        using var doc = JsonDocument.Parse(snapshot.SnapshotJson!);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("dataModel").ValueKind);
        Assert.Contains(doc.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetString()!.Contains("dataModelJson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V08PushRoutesIntoMainSurfaceAndKeepsA2UiEvent()
    {
        var (viewModel, ws) = CreateViewModel();

        await ApplyCanvasEnvelopeAsync(viewModel, new WsServerEnvelope
        {
            Type = "a2ui_push",
            RequestId = "req1",
            SessionId = "sess",
            SurfaceId = "legacy",
            Frames = """{"type":"button","id":"save","label":"Save"}"""
        });

        var surface = Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("main", surface.SurfaceId);
        Assert.Equal("main", viewModel.CanvasFrames.Single().SurfaceId);

        await viewModel.CanvasFrames.Single().ActivateCommand.ExecuteAsync(null);

        var evt = LastSentEnvelope(ws);
        Assert.Equal("a2ui_event", evt.Type);
        Assert.Equal("main", evt.SurfaceId);
        Assert.Equal("save", evt.ComponentId);
    }

    [Fact]
    public async Task ApplyCanvasEnvelope_V09UnsupportedAdvancedComponentRecordsDiagnosticAndRendersFallback()
    {
        var (viewModel, ws) = CreateViewModel();

        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", ["""{"type":"Video","id":"demo","src":"demo.mp4"}"""]));

        var surface = Assert.Single(viewModel.CanvasSurfaces);
        Assert.Equal("demo", surface.Components.Single().Id);
        Assert.Contains(surface.Diagnostics, diagnostic => diagnostic.Contains("Video", StringComparison.OrdinalIgnoreCase));
        Assert.True(LastSentEnvelope(ws).Success);
    }

    private static WsServerEnvelope CreateSurfaceEnvelope(string surfaceId, string title, string[] components, string? dataModelJson = null)
        => new()
        {
            Type = "a2ui_create_surface",
            Operation = "createSurface",
            RequestId = "create-" + surfaceId,
            SessionId = "sess",
            SurfaceId = surfaceId,
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            SurfaceTitle = title,
            Components = components,
            DataModelJson = dataModelJson
        };

    private static string TextComponent(string id, string text)
        => $$"""{"type":"Text","id":"{{id}}","text":"{{text}}"}""";

    private static string ButtonComponent(string id, string label)
        => $$"""{"type":"Button","id":"{{id}}","label":"{{label}}"}""";

    private (MainWindowViewModel ViewModel, TestWebSocket Socket) CreateViewModel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openclaw-companion-canvas-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        var client = new GatewayWebSocketClient();
        var ws = new TestWebSocket();
        client.SetConnectedSocketForTest(ws);
        return (new MainWindowViewModel(new SettingsStore(dir), client), ws);
    }

    private static async Task ApplyCanvasEnvelopeAsync(MainWindowViewModel viewModel, WsServerEnvelope envelope)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyCanvasEnvelopeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(viewModel, [envelope]);
        Assert.NotNull(task);
        await task!;
    }

    private static async Task SendCanvasReadyAsync(MainWindowViewModel viewModel)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "SendCanvasReadyAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(viewModel, []);
        Assert.NotNull(task);
        await task!;
    }

    private static WsClientEnvelope LastSentEnvelope(TestWebSocket ws)
    {
        var payload = Encoding.UTF8.GetString(ws.Sent.Last());
        return JsonSerializer.Deserialize(payload, CoreJsonContext.Default.WsClientEnvelope)
            ?? throw new InvalidOperationException("Sent payload was not a websocket envelope.");
    }
}
