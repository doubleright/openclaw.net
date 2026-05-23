using System.Linq;
using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace OpenClaw.Tests;

public sealed class DashboardStaticAssetTests
{
    [Fact]
    public void DashboardProject_DoesNotUseAggressiveLinkTrimming()
    {
        var project = LoadDashboardProject();
        var trimMode = project.Descendants("TrimMode").SingleOrDefault()?.Value;

        Assert.Equal("partial", trimMode);
    }

    [Fact]
    public void DashboardProject_DoesNotUseInvariantGlobalization()
    {
        var project = LoadDashboardProject();
        var invariantGlobalization = project.Descendants("InvariantGlobalization").SingleOrDefault()?.Value;

        Assert.Equal("false", invariantGlobalization);
    }

    [Fact]
    public void DashboardStartup_ReplacesMudBlazorResourceManagerLocalizationInterceptor()
    {
        var program = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Program.cs"));

        Assert.Contains("AddLocalizationInterceptor<DashboardMudLocalizationInterceptor>()", program);
        Assert.Contains("ILocalizationInterceptor", program);
    }

    [Fact]
    public void DashboardStartup_ConfiguresHttpClientBaseAddress()
    {
        var program = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Program.cs"));

        Assert.Contains("new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) }", program);
    }

    [Fact]
    public void DashboardNavigation_UsesBaseRelativeLinks()
    {
        var navMenu = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Layout", "NavMenu.razor"));

        Assert.DoesNotContain("Href=\"/", navMenu);
        Assert.Contains("Href=\"integration\"", navMenu);
        Assert.Contains("Href=\"heartbeat\"", navMenu);
    }

    [Fact]
    public void DashboardNavigation_RendersOnlyForAuthenticatedUsers()
    {
        var navMenu = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Layout", "NavMenu.razor"));
        var authenticatedBlock = ExtractIfBlock(navMenu, "@if (Auth.IsAuthenticated)");

        Assert.Contains("Href=\"\"", authenticatedBlock);
        Assert.Contains("Href=\"observability\"", authenticatedBlock);
        Assert.Contains("Href=\"migration\"", authenticatedBlock);
        Assert.Contains("Href=\"integration\"", authenticatedBlock);
        Assert.Contains("Href=\"sessions\"", authenticatedBlock);
        Assert.Contains("Auth.HasRole(\"admin\")", authenticatedBlock);
        Assert.Contains("Auth.HasRole(\"operator\")", authenticatedBlock);
    }

    [Fact]
    public void LoginDialog_MasksTokenInputs()
    {
        var loginDialog = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Components", "LoginDialog.razor"));

        Assert.Contains("InputType=\"InputType.Password\"", ExtractMudTextField(loginDialog, "@bind-Value=\"_token\""));
        Assert.Contains("InputType=\"InputType.Password\"", ExtractMudTextField(loginDialog, "@bind-Value=\"_bootstrapToken\""));
    }

    [Fact]
    public void AuthState_AllowsGatewayNullUsername()
    {
        var authState = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "AuthState.cs"));
        var mainLayout = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Layout", "MainLayout.razor"));

        Assert.Contains("string? Username", authState);
        Assert.DoesNotContain("string Username", authState);
        Assert.Contains("Auth.CurrentAuth?.Username ??", mainLayout);
    }

    [Fact]
    public void DashboardApiService_NormalizesRelativeApiUrlsToGatewayRoot()
    {
        var apiService = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Services", "ApiService.cs"));

        Assert.Contains("NormalizeApiUrl", apiService);
        Assert.Contains("return string.Concat(\"/\", url);", apiService);
    }

    [Fact]
    public void DashboardPages_DoNotCallStaleAdminApiRoutes()
    {
        var pagesDirectory = Path.Combine(GetDashboardProjectDirectory(), "Pages");
        var pageSources = Directory.GetFiles(pagesDirectory, "*.razor")
            .Select(File.ReadAllText)
            .ToArray();
        var dashboardSource = string.Join("\n", pageSources);

        Assert.DoesNotContain("admin/learning\"", dashboardSource);
        Assert.DoesNotContain("admin/learning/{", dashboardSource);
        Assert.DoesNotContain("/admin/channels\"", dashboardSource);
        Assert.DoesNotContain("/admin/channels/{", dashboardSource);
        Assert.DoesNotContain("/admin/allowlists", dashboardSource);
        Assert.DoesNotContain("admin/doctor", dashboardSource);
        Assert.DoesNotContain("/admin/channels/whatsapp/auth/restart", dashboardSource);
        Assert.Contains("/admin/channels/whatsapp/restart", dashboardSource);
        Assert.Contains("TryGetJsonAsync(\"doctor\")", dashboardSource);
    }

    [Fact]
    public void ChannelsPage_OffersCurrentEnableConfigureAndRestartActions()
    {
        var channelsPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Channels.razor"));

        Assert.Contains("MudSwitch", channelsPage);
        Assert.Contains("SaveWhatsAppSetup", channelsPage);
        Assert.Contains("RestartWhatsApp", channelsPage);
        Assert.Contains("admin/channels/whatsapp/setup", channelsPage);
        Assert.Contains("admin/channels/whatsapp/restart", channelsPage);
        Assert.Contains("L[\"channels.configure\"]", channelsPage);
        Assert.Contains("L[\"common.actions\"]", channelsPage);
    }

    [Fact]
    public void DashboardLayout_WaitsForAuthBeforeRenderingRoutedPages()
    {
        var layout = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Layout", "MainLayout.razor"));

        Assert.Contains("_initialized", layout);
        Assert.Contains("@Body", layout);
        Assert.Contains("await Auth.SyncAuth();", layout);
        Assert.Contains("_initialized = true;", layout);
    }

    [Fact]
    public void OverviewPage_DoesNotRenderUnknownHealthFallback()
    {
        var overviewPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Overview.razor"));

        Assert.DoesNotContain("?? \"unknown\").ToLowerInvariant()", overviewPage);
        Assert.DoesNotContain("raw.ToUpperInvariant()", overviewPage);
        Assert.Contains("ShowHealthChip", overviewPage);
    }

    [Fact]
    public void OverviewPage_RendersCurrentDoctorCheckFields()
    {
        var overviewPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Overview.razor"));

        Assert.Contains("ReadString(item, \"label\", \"name\", \"check\", \"id\")", overviewPage);
        Assert.Contains("ReadString(item, \"summary\", \"message\", \"detail\", \"description\")", overviewPage);
        Assert.Contains("ReadString(item, \"nextStep\")", overviewPage);
    }

    [Fact]
    public void DashboardLayout_OffsetsRoutedPagesBelowAppBar()
    {
        var layout = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Layout", "MainLayout.razor"));
        var css = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "wwwroot", "css", "app.css"));

        Assert.Contains("dashboard-main", layout);
        Assert.Contains(".dashboard-main", css);
        Assert.Contains("padding-top", css);
    }

    [Fact]
    public void HeartbeatPage_DeserializesPulseEventsFromListResponse()
    {
        var heartbeatPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Heartbeat.razor"));
        var heartbeatModels = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "HeartbeatConfig.cs"));

        Assert.DoesNotContain("GetAsync<List<PulseEvent>>(\"admin/pulse/events\")", heartbeatPage);
        Assert.Contains("GetAsync<PulseEventListResponse>(\"admin/pulse/events\")", heartbeatPage);
        Assert.Contains("PulseEventListResponse", heartbeatModels);
    }

    [Fact]
    public void HeartbeatStatusModel_MatchesCurrentAdminStatusResponseShape()
    {
        var heartbeatModels = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "HeartbeatConfig.cs"));
        var heartbeatPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Heartbeat.razor"));

        Assert.DoesNotContain("DateTime? LastRun", heartbeatModels);
        Assert.Contains("HeartbeatRunStatus? LastRun", heartbeatModels);
        Assert.Contains("LastRun?.LastRunAtUtc", heartbeatPage);
    }

    [Fact]
    public void HeartbeatPage_UsesCurrentPulseRunRouteAndAvoidsFocusedSvgAdornment()
    {
        var heartbeatPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Heartbeat.razor"));

        Assert.Contains("PostRawAsync(\"admin/pulse/run\"", heartbeatPage);
        Assert.DoesNotContain("admin/pulse/trigger", heartbeatPage);
        Assert.DoesNotContain("AdornmentIcon=\"@Icons.Material.Filled.Schedule\"", heartbeatPage);
    }

    [Fact]
    public void AutomationPage_UsesCurrentAdminAutomationResponseShape()
    {
        var automationPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Automation.razor"));
        var automationModels = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "AutomationConfig.cs"));

        Assert.DoesNotContain("GetAsync<List<AutomationConfig>>(\"admin/automations\")", automationPage);
        Assert.Contains("GetAsync<AutomationListResponse>(\"admin/automations\")", automationPage);
        Assert.Contains("admin/automations/preview", automationPage);
        Assert.Contains("Api.PutRawAsync", automationPage);
        Assert.Contains("AutomationListResponse", automationModels);
    }

    [Fact]
    public void MemoryPage_UsesCurrentAdminMemoryResponseShape()
    {
        var memoryPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Memory.razor"));
        var memoryModels = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "MemoryNote.cs"));

        Assert.DoesNotContain("GetAsync<List<MemoryNote>>(\"admin/memory/notes\")", memoryPage);
        Assert.Contains("GetAsync<MemoryNoteListResponse>(\"admin/memory/notes\")", memoryPage);
        Assert.DoesNotContain("GetAsync<MemoryNote>($\"admin/memory/notes/", memoryPage);
        Assert.Contains("GetAsync<MemoryNoteDetailResponse>($\"admin/memory/notes/", memoryPage);
        Assert.Contains("MemoryNoteListResponse", memoryModels);
        Assert.Contains("MemoryNoteDetailResponse", memoryModels);
        Assert.Contains("DateTimeOffset UpdatedAtUtc", memoryModels);
    }

    [Fact]
    public void SettingsPage_UsesCurrentAdminSettingsResponseShape()
    {
        var settingsPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Settings.razor"));

        Assert.Contains("root.GetProperty(\"settings\")", settingsPage);
        Assert.Contains("_settingsSnapshotJson", settingsPage);
        Assert.Contains("sessionTokenBudget", settingsPage);
        Assert.Contains("requireToolApproval", settingsPage);
        Assert.Contains("readOnlyMode", settingsPage);
        Assert.Contains("enableCompaction", settingsPage);
        Assert.DoesNotContain("channelPolicies", settingsPage);
        Assert.DoesNotContain("allowFileWrite", settingsPage);
        Assert.DoesNotContain("allowFileRead", settingsPage);
        Assert.DoesNotContain("readRoot", settingsPage);
        Assert.DoesNotContain("writeRoot", settingsPage);
    }

    [Fact]
    public void LearningPage_UsesCurrentAdminProposalResponseShape()
    {
        var learningPage = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Pages", "Learning.razor"));
        var learningModels = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Models", "LearningProposal.cs"));

        Assert.DoesNotContain("p.Type", learningPage);
        Assert.DoesNotContain("p.CreatedAt)", learningPage);
        Assert.DoesNotContain("_selected.Type", learningPage);
        Assert.DoesNotContain("_selected.CreatedAt)", learningPage);
        Assert.DoesNotContain("_selected.Diff", learningPage);
        Assert.Contains("p.Kind", learningPage);
        Assert.Contains("CreatedAtUtc", learningPage);
        Assert.Contains("GetAsync<LearningProposalDetailResponse>($\"admin/learning/proposals/", learningPage);
        Assert.Contains("RenderProposalChanges", learningPage);
        Assert.Contains("CanRollback", learningPage);
        Assert.Contains("CanApproveOrReject", learningPage);
        Assert.Contains("string? Kind", learningModels);
        Assert.Contains("DateTimeOffset CreatedAtUtc", learningModels);
        Assert.Contains("LearningProposalDetailResponse", learningModels);
    }

    [Fact]
    public void DashboardSource_DoesNotUseEvalInterop()
    {
        var dashboardDirectory = GetDashboardProjectDirectory();
        var sourceFiles = Directory.GetFiles(dashboardDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && (path.EndsWith(".razor", StringComparison.Ordinal)
                    || path.EndsWith(".cs", StringComparison.Ordinal)
                    || path.EndsWith(".html", StringComparison.Ordinal)
                    || path.EndsWith(".js", StringComparison.Ordinal)))
            .ToArray();
        var source = string.Join("\n", sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("InvokeVoidAsync(\"eval\"", source);
        Assert.DoesNotContain("InvokeAsync<string?>(\"eval\"", source);
    }

    [Fact]
    public void TokenRevealDialog_LocalizesUserFacingText()
    {
        var tokenRevealDialog = File.ReadAllText(Path.Combine(GetDashboardProjectDirectory(), "Components", "TokenRevealDialog.razor"));

        Assert.DoesNotContain("This token is shown", tokenRevealDialog);
        Assert.DoesNotContain(">Copy<", tokenRevealDialog.ReplaceLineEndings(string.Empty));
        Assert.Contains("L[\"governance.tokenShownOncePrefix\"]", tokenRevealDialog);
        Assert.Contains("L[\"governance.tokenShownOnceEmphasis\"]", tokenRevealDialog);
        Assert.Contains("L[\"governance.tokenShownOnceSuffix\"]", tokenRevealDialog);
        Assert.Contains("L[\"common.copy\"]", tokenRevealDialog);
    }

    [Fact]
    public void DashboardLocalizationFiles_ContainAllReferencedKeys()
    {
        var dashboardDirectory = GetDashboardProjectDirectory();
        var sourceFiles = Directory.GetFiles(dashboardDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && (path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal)))
            .ToArray();
        var source = string.Join("\n", sourceFiles.Select(File.ReadAllText));
        var keys = System.Text.RegularExpressions.Regex.Matches(source, "L\\[\\\"([^\\\"]+)\\\"\\]")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var locale in new[] { "en-US", "zh-CN" })
        {
            var localeJson = File.ReadAllText(Path.Combine(dashboardDirectory, "wwwroot", "locales", $"{locale}.json"));
            using var doc = System.Text.Json.JsonDocument.Parse(localeJson);
            var translations = new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenLocalizationJson(doc.RootElement, string.Empty, translations);
            var missing = keys.Where(key => !translations.ContainsKey(key)).Order(StringComparer.Ordinal).ToArray();

            Assert.True(missing.Length == 0, $"{locale} is missing Dashboard localization keys: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void DashboardBuildOutput_IncludesCanonicalBlazorRuntimeAssets()
    {
        var gatewayBinPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "OpenClaw.Gateway", "bin"));
        var candidatePaths = Directory.Exists(gatewayBinPath)
            ? Directory.EnumerateDirectories(gatewayBinPath)
                .SelectMany(configurationDirectory => Directory.EnumerateDirectories(configurationDirectory, "net*")
                    .Select(tfmDirectory => new
                    {
                        FrameworkPath = Path.Combine(tfmDirectory, "wwwroot", "dashboard", "_framework"),
                        LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(tfmDirectory)
                    }))
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .Select(candidate => candidate.FrameworkPath)
                .ToArray()
            : [];
        var gatewayFrameworkPath = candidatePaths.FirstOrDefault(Directory.Exists);

        Assert.True(
            gatewayFrameworkPath is not null,
            $"Could not find dashboard framework output under {gatewayBinPath}. Checked: {string.Join(", ", candidatePaths)}");
        Assert.True(
            File.Exists(Path.Combine(gatewayFrameworkPath, "blazor.webassembly.js")),
            $"Missing blazor.webassembly.js in {gatewayFrameworkPath}");
        Assert.True(
            File.Exists(Path.Combine(gatewayFrameworkPath, "dotnet.js")),
            $"Missing dotnet.js in {gatewayFrameworkPath}");
    }

    [Fact]
    public async Task DashboardStaticAssets_AreServedBeforeSpaFallback()
    {
        var dashboardPath = Path.Combine(Path.GetTempPath(), "openclaw-dashboard-static-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dashboardPath, "css"));
        await File.WriteAllTextAsync(Path.Combine(dashboardPath, "index.html"), "<!doctype html><title>Dashboard</title>");
        await File.WriteAllTextAsync(Path.Combine(dashboardPath, "css", "app.css"), ".dashboard { color: rebeccapurple; }");

        await using var app = await CreateAppAsync(dashboardPath);

        var staticAssetResponse = await app.GetTestClient().GetAsync("/dashboard/css/app.css");

        Assert.Equal(HttpStatusCode.OK, staticAssetResponse.StatusCode);
        Assert.Equal("text/css", staticAssetResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(".dashboard { color: rebeccapurple; }", await staticAssetResponse.Content.ReadAsStringAsync());

        var fallbackResponse = await app.GetTestClient().GetAsync("/dashboard/unknown-route");

        Assert.Equal(HttpStatusCode.OK, fallbackResponse.StatusCode);
        Assert.Contains("<!doctype html>", await fallbackResponse.Content.ReadAsStringAsync());
    }

    private static string ExtractMudTextField(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find MudTextField marker {marker}.");
        var startIndex = source.LastIndexOf("<MudTextField", markerIndex, StringComparison.Ordinal);
        var endIndex = source.IndexOf("/>", markerIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex >= markerIndex, $"Could not find MudTextField containing {marker}.");
        return source[startIndex..(endIndex + 2)];
    }

    private static string ExtractIfBlock(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find block marker {marker}.");
        var startIndex = source.IndexOf('{', markerIndex);
        Assert.True(startIndex >= 0, $"Could not find block start for {marker}.");

        var depth = 0;
        for (var index = startIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
                depth--;

            if (depth == 0)
                return source[startIndex..(index + 1)];
        }

        throw new InvalidOperationException($"Could not find block end for {marker}.");
    }

    private static XDocument LoadDashboardProject()
    {
        return XDocument.Load(Path.Combine(GetDashboardProjectDirectory(), "OpenClaw.Dashboard.csproj"));
    }

    private static string GetDashboardProjectDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "OpenClaw.Dashboard"));
    }

    private static void FlattenLocalizationJson(System.Text.Json.JsonElement element, string prefix, Dictionary<string, string> result)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : string.Concat(prefix, ".", property.Name);
                FlattenLocalizationJson(property.Value, key, result);
            }

            return;
        }

        result[prefix] = element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();
    }

    private static async Task<WebApplication> CreateAppAsync(string dashboardPath)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.Map("/dashboard", dashboardApp =>
        {
            dashboardApp.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(dashboardPath),
                ContentTypeProvider = new FileExtensionContentTypeProvider()
            });

            dashboardApp.Run(async ctx =>
            {
                if (ctx.Request.Path.HasValue && Path.HasExtension(ctx.Request.Path.Value))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var htmlPath = Path.Combine(dashboardPath, "index.html");
                if (File.Exists(htmlPath))
                {
                    ctx.Response.ContentType = "text/html";
                    await ctx.Response.SendFileAsync(htmlPath);
                    return;
                }

                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        });

        await app.StartAsync();
        return app;
    }
}
