using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isPluginsBusy;

    [ObservableProperty]
    private string _pluginsStatus = "Plugins and channels not loaded.";

    public ObservableCollection<PluginRow> PluginRows { get; } = [];
    public ObservableCollection<CompatibilityRow> CompatibilityRows { get; } = [];
    public ObservableCollection<ChannelReadinessRow> PluginChannelRows { get; } = [];
    public bool HasPluginRows => PluginRows.Count > 0;
    public bool HasCompatibilityRows => CompatibilityRows.Count > 0;
    public bool HasPluginChannelRows => PluginChannelRows.Count > 0;

    [RelayCommand]
    private async Task LoadPluginsAsync()
    {
        if (IsPluginsBusy)
            return;

        IsPluginsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => PluginsStatus = status);
            if (client is null)
                return;

            var plugins = await client.GetIntegrationPluginsAsync(CancellationToken.None);
            var catalog = await client.GetCompatibilityCatalogAsync(null, null, null, CancellationToken.None);
            var dashboard = await client.GetIntegrationDashboardAsync(CancellationToken.None);

            ReplaceItems(PluginRows, plugins.Items.Select(PluginRow.FromSnapshot));
            ReplaceItems(CompatibilityRows, catalog.Catalog.Items.Take(100).Select(CompatibilityRow.FromEntry));
            ReplaceItems(PluginChannelRows, dashboard.Operator.Channels.Items.Select(ChannelReadinessRow.FromDto));
            OnPropertyChanged(nameof(HasPluginRows));
            OnPropertyChanged(nameof(HasCompatibilityRows));
            OnPropertyChanged(nameof(HasPluginChannelRows));
            PluginsStatus = $"Loaded {PluginRows.Count} plugin(s), {CompatibilityRows.Count} compatibility item(s), and {PluginChannelRows.Count} channel(s).";
        }
        catch (OperationCanceledException ex)
        {
            PluginsStatus = $"Plugins load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            PluginsStatus = $"Plugins load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            PluginsStatus = $"Plugins load failed: {ex.Message}";
        }
        finally
        {
            IsPluginsBusy = false;
        }
    }
}

public sealed class PluginRow
{
    public required string PluginId { get; init; }
    public required string Origin { get; init; }
    public required string Status { get; init; }
    public required string Trust { get; init; }
    public required string Compatibility { get; init; }
    public required string SurfaceCounts { get; init; }
    public required string Detail { get; init; }

    public static PluginRow FromSnapshot(PluginHealthSnapshot item)
        => new()
        {
            PluginId = item.PluginId,
            Origin = item.Origin,
            Status = item.Quarantined ? "quarantined" : item.Disabled ? "disabled" : item.Loaded ? "loaded" : "not loaded",
            Trust = item.TrustLevel,
            Compatibility = item.CompatibilityStatus,
            SurfaceCounts = $"tools {item.ToolCount} · channels {item.ChannelCount} · providers {item.ProviderCount}",
            Detail = item.LastError ?? item.PendingReason ?? item.TrustReason
        };
}

public sealed class CompatibilityRow
{
    public required string Subject { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
    public required string Category { get; init; }
    public required string Summary { get; init; }

    public static CompatibilityRow FromEntry(CompatibilityCatalogEntry item)
        => new()
        {
            Subject = item.Subject,
            Status = item.CompatibilityStatus,
            Kind = item.Kind,
            Category = item.Category,
            Summary = item.Summary
        };
}
