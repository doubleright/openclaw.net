using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;
using Xunit;

namespace OpenClaw.Tests;

public sealed class A2UiFrameValidatorTests
{
    [Fact]
    public void ValidateJsonl_AcceptsSupportedV08Frames()
    {
        var frames = string.Join('\n',
            """{"type":"text","id":"txt","text":"Hello"}""",
            """{"type":"button","id":"ok","label":"OK"}""",
            """{"type":"table","id":"tbl","columns":["Name"],"rows":[["A"]]}""",
            """{"type":"progress","id":"prog","value":0.5}""");

        var result = A2UiFrameValidator.ValidateJsonl(frames, maxFrames: 10, maxBytes: 4096);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.FrameCount);
    }

    [Fact]
    public void ValidateJsonl_RejectsInvalidJson()
    {
        var result = A2UiFrameValidator.ValidateJsonl("""{"type":"text","id":"bad","text":""", maxFrames: 10, maxBytes: 4096);

        Assert.False(result.IsValid);
        Assert.Contains("not valid JSON", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateJsonl_RejectsCreateSurfaceV09Frame()
    {
        var result = A2UiFrameValidator.ValidateJsonl("""{"type":"createSurface","id":"surface"}""", maxFrames: 10, maxBytes: 4096);

        Assert.False(result.IsValid);
        Assert.Contains("createSurface", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateJsonl_RejectsFrameAndSizeLimits()
    {
        var tooMany = A2UiFrameValidator.ValidateJsonl(
            """
            {"type":"text","id":"a","text":"A"}
            {"type":"text","id":"b","text":"B"}
            """,
            maxFrames: 1,
            maxBytes: 4096);

        var tooLarge = A2UiFrameValidator.ValidateJsonl("""{"type":"text","id":"a","text":"A"}""", maxFrames: 10, maxBytes: 4);

        Assert.False(tooMany.IsValid);
        Assert.Contains("exceeds 1 frames", tooMany.Error, StringComparison.Ordinal);
        Assert.False(tooLarge.IsValid);
        Assert.Contains("exceeds 4 bytes", tooLarge.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateJsonl_RejectsMissingTypeSpecificFields()
    {
        var result = A2UiFrameValidator.ValidateJsonl("""{"type":"select","id":"choice"}""", maxFrames: 10, maxBytes: 4096);

        Assert.False(result.IsValid);
        Assert.Contains("options", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateJsonl_PreservesV08ComponentAliases()
    {
        var frames = string.Join('\n',
            """{"type":"markdown","id":"md","text":"**Hello**"}""",
            """{"type":"input","id":"name"}""",
            """{"type":"select","id":"choice","options":["A"]}""",
            """{"type":"checklist","id":"checks","options":["A"]}""",
            """{"type":"image","id":"img","url":"https://example.test/a.png"}""",
            """{"type":"chart","id":"chart","data":{}}""");

        var result = A2UiFrameValidator.ValidateJsonl(frames, maxFrames: 10, maxBytes: 4096);

        Assert.True(result.IsValid);
        Assert.Equal(6, result.FrameCount);
    }

    [Fact]
    public void ValidateV09_AcceptsValidCreateSurface()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "createSurface",
            SurfaceId = "surface-1",
            SupportedCatalogIds = [A2UiCatalogRegistry.AGenUiCatalogId]
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_AcceptsValidUpdateComponentsJsonStringArray()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateComponents",
            SurfaceId = "surface-1",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = ["""{"type":"Text","id":"t1","text":"Hello"}""", """{"type":"Button","id":"b1","label":"OK"}"""]
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_RejectsMalformedComponentString()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateComponents",
            SurfaceId = "surface-1",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = ["{\"type\":\"Text\""]
        });

        Assert.False(result.IsValid);
        Assert.Contains("not valid JSON", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_UpdateDataModelAcceptsJsonObject()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateDataModel",
            SurfaceId = "surface-1",
            DataModelJson = """{"count":1}"""
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_UpdateDataModelRejectsMissingSurfaceId()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateDataModel",
            DataModelJson = """{"count":1}"""
        });

        Assert.False(result.IsValid);
        Assert.Contains("surfaceId", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_UpdateDataModelRejectsMalformedJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateDataModel",
            SurfaceId = "surface-1",
            DataModelJson = "{\"count\":"
        });

        Assert.False(result.IsValid);
        Assert.Contains("not valid JSON", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_UpdateDataModelRejectsNonObjectJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateDataModel",
            SurfaceId = "surface-1",
            DataModelJson = """["count"]"""
        });

        Assert.False(result.IsValid);
        Assert.Contains("JSON object", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_DeleteSurfaceAcceptsSurfaceId()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "deleteSurface",
            SurfaceId = "surface-1"
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_DeleteSurfaceRejectsMissingSurfaceId()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "deleteSurface"
        });

        Assert.False(result.IsValid);
        Assert.Contains("surfaceId", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_SyncUIToDataAcceptsSurfaceIdWithoutDataModelJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "syncUIToData",
            SurfaceId = "surface-1"
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_SyncUIToDataAcceptsOptionalDataModelJsonObject()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "syncUIToData",
            SurfaceId = "surface-1",
            DataModelJson = """{"enabled":true}"""
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_SyncUIToDataRejectsInvalidOptionalDataModelJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "syncUIToData",
            SurfaceId = "surface-1",
            DataModelJson = "{\"enabled\":"
        });

        Assert.False(result.IsValid);
        Assert.Contains("not valid JSON", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_SyncUIToDataRejectsNonObjectOptionalDataModelJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "syncUIToData",
            SurfaceId = "surface-1",
            DataModelJson = "true"
        });

        Assert.False(result.IsValid);
        Assert.Contains("JSON object", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_ActionAcceptsSurfaceIdAndAction()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "action",
            SurfaceId = "surface-1",
            Action = "submit"
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_ActionRejectsMissingAction()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "action",
            SurfaceId = "surface-1"
        });

        Assert.False(result.IsValid);
        Assert.Contains("action", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_ActionRejectsMissingSurfaceId()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "action",
            Action = "submit"
        });

        Assert.False(result.IsValid);
        Assert.Contains("surfaceId", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_ActionRejectsNonObjectParametersJson()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "action",
            SurfaceId = "surface-1",
            Action = "submit",
            ParametersJson = "[]"
        });

        Assert.False(result.IsValid);
        Assert.Contains("JSON object", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("error", null)]
    [InlineData(null, "E_A2UI")]
    public void ValidateV09_ErrorAcceptsErrorOrDiagnosticCode(string? error, string? diagnosticCode)
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "error",
            Error = error,
            DiagnosticCode = diagnosticCode
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateV09_ErrorRejectsWhenErrorAndDiagnosticCodeAreAbsent()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "error"
        });

        Assert.False(result.IsValid);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnosticCode", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_RejectsUnsupportedCatalogIds()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "createSurface",
            SurfaceId = "surface-1",
            CatalogId = "urn:a2ui:catalog:unknown"
        });

        Assert.False(result.IsValid);
        Assert.Contains("catalog", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateV09_RejectsUnsupportedComponentTypes()
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateComponents",
            SurfaceId = "surface-1",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = ["""{"type":"UnsupportedWidget","id":"bad"}"""]
        });

        Assert.False(result.IsValid);
        Assert.Contains("UnsupportedWidget", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Image")]
    [InlineData("Icon")]
    [InlineData("Divider")]
    [InlineData("Video")]
    [InlineData("AudioPlayer")]
    [InlineData("Markdown")]
    [InlineData("Button")]
    [InlineData("TextField")]
    [InlineData("CheckBox")]
    [InlineData("Slider")]
    [InlineData("ChoicePicker")]
    [InlineData("DateTimeInput")]
    [InlineData("Row")]
    [InlineData("Column")]
    [InlineData("Card")]
    [InlineData("List")]
    [InlineData("Tabs")]
    [InlineData("Modal")]
    [InlineData("Table")]
    [InlineData("Carousel")]
    [InlineData("Web")]
    [InlineData("RichText")]
    [InlineData("Chart")]
    public void ValidateV09_AcceptsEveryTargetAGenUiComponentType(string componentType)
    {
        var result = A2UiV09MessageValidator.Validate(new WsServerEnvelope
        {
            Type = "canvas",
            Operation = "updateComponents",
            SurfaceId = "surface-1",
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            Components = [$$"""{"type":"{{componentType}}","id":"component-1"}"""]
        });

        Assert.True(result.IsValid, result.Error);
    }
}
