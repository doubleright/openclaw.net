using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenClaw.Companion;
using OpenClaw.Companion.Services;
using OpenClaw.Companion.ViewModels;
using OpenClaw.Companion.Views;
using OpenClaw.Core.Canvas;
using OpenClaw.Core.Models;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(OpenClaw.Tests.CompanionAvaloniaTestApp))]

namespace OpenClaw.Tests;

public sealed class CompanionAvaloniaTestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class CompanionCanvasUiTests : IDisposable
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

    [AvaloniaFact]
    public async Task MainWindow_RendersSurfaceSelectorAndActiveSurfaceComponents()
    {
        var viewModel = CreateViewModel();
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("alpha", "Alpha", [TextComponent("alpha-text", "Alpha ready")]));
        await ApplyCanvasEnvelopeAsync(viewModel, CreateSurfaceEnvelope("beta", "Beta", [ButtonComponent("beta-save", "Save beta")]));

        var window = new MainWindow
        {
            Width = 900,
            Height = 600,
            DataContext = viewModel
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
            var canvasTab = GetTabByHeader(tabControl, "Canvas");
            tabControl.SelectedItem = canvasTab;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(canvasTab, tabControl.SelectedItem);

            var selector = window.FindControl<ComboBox>("CanvasSurfaceSelector");
            Assert.NotNull(selector);
            Assert.True(selector!.IsVisible);
            Assert.Equal(viewModel.CanvasSurfaces, selector.ItemsSource);
            Assert.Equal(viewModel.ActiveCanvasSurface, selector.SelectedItem);
            Assert.Contains(viewModel.CanvasSurfaces, surface => surface.Title == "Alpha");
            Assert.Contains(viewModel.CanvasSurfaces, surface => surface.Title == "Beta");

            var componentHost = window.FindControl<ItemsControl>("CanvasComponentHost");
            Assert.NotNull(componentHost);
            Assert.Equal(viewModel.ActiveCanvasSurface!.Components, componentHost!.ItemsSource);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => string.Equals(button.Content?.ToString(), "Save beta", StringComparison.Ordinal));

            selector.SelectedItem = viewModel.CanvasSurfaces.Single(surface => surface.SurfaceId == "alpha");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("alpha", viewModel.ActiveCanvasSurface?.SurfaceId);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => string.Equals(text.Text, "Alpha ready", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_RendersRuntimeConsoleShellAndNavigationSections()
    {
        var viewModel = CreateViewModel();
        var window = new MainWindow
        {
            Width = 1100,
            Height = 760,
            DataContext = viewModel
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.Equal(Dock.Left, tabControl.TabStripPlacement);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => string.Equals(text.Text, "OpenClaw.NET Companion", StringComparison.Ordinal));

            var headers = tabControl.Items.OfType<TabItem>().Select(static item => item.Header?.ToString()).ToArray();
            Assert.Contains("Home", headers);
            Assert.Contains("Sessions", headers);
            Assert.Contains("Runtime Events", headers);
            Assert.Contains("Plugins & Channels", headers);
            Assert.Contains("Payment Lab", headers);

            var sessionsTab = GetTabByHeader(tabControl, "Sessions");
            tabControl.SelectedItem = sessionsTab;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(sessionsTab, tabControl.SelectedItem);
            Assert.Equal(Array.IndexOf(headers, "Sessions"), viewModel.SelectedSectionIndex);
            Assert.Same(sessionsTab.Content, tabControl.SelectedContent);
        }
        finally
        {
            window.Close();
        }
    }

    private MainWindowViewModel CreateViewModel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openclaw-companion-canvas-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        var client = new GatewayWebSocketClient();
        client.SetConnectedSocketForTest(new TestWebSocket());
        return new MainWindowViewModel(new SettingsStore(dir), client);
    }

    private static TabItem GetTabByHeader(TabControl tabControl, string header)
        => tabControl.Items
            .OfType<TabItem>()
            .Single(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

    private static WsServerEnvelope CreateSurfaceEnvelope(string surfaceId, string title, string[] components)
        => new()
        {
            Type = "a2ui_create_surface",
            Operation = "createSurface",
            RequestId = "create-" + surfaceId,
            SessionId = "sess",
            SurfaceId = surfaceId,
            CatalogId = A2UiCatalogRegistry.AGenUiCatalogId,
            SurfaceTitle = title,
            Components = components
        };

    private static string TextComponent(string id, string text)
        => $$"""{"type":"Text","id":"{{id}}","text":"{{text}}"}""";

    private static string ButtonComponent(string id, string label)
        => $$"""{"type":"Button","id":"{{id}}","label":"{{label}}"}""";

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
}
