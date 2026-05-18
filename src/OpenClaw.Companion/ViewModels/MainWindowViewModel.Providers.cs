using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isProvidersBusy;

    [ObservableProperty]
    private string _providersStatus = "Models and providers not loaded.";

    [ObservableProperty]
    private string _modelProfilesSummary = "Model profiles not loaded.";

    public ObservableCollection<ProviderRouteRow> ProviderRouteRows { get; } = [];
    public ObservableCollection<ToolPresetRow> ToolPresetRows { get; } = [];
    public bool HasProviderRouteRows => ProviderRouteRows.Count > 0;
    public bool HasToolPresetRows => ToolPresetRows.Count > 0;

    [RelayCommand]
    private async Task LoadProvidersAsync()
    {
        if (IsProvidersBusy)
            return;

        IsProvidersBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => ProvidersStatus = status);
            if (client is null)
                return;

            var providers = await client.GetIntegrationProvidersAsync(50, CancellationToken.None);
            var presets = await client.ListToolPresetsAsync(CancellationToken.None);
            ReplaceItems(ProviderRouteRows, providers.Routes.Select(ProviderRouteRow.FromSnapshot));
            ReplaceItems(ToolPresetRows, presets.Items.Select(ToolPresetRow.FromPreset));
            OnPropertyChanged(nameof(HasProviderRouteRows));
            OnPropertyChanged(nameof(HasToolPresetRows));
            ModelProfilesSummary = providers.ModelProfiles is null
                ? "No model profile status reported."
                : $"Routes: {providers.Routes.Count}; policies: {providers.Policies.Count}; recent turns: {providers.RecentTurns.Count}.";
            ProvidersStatus = $"Loaded {ProviderRouteRows.Count} provider route(s) and {ToolPresetRows.Count} tool preset(s).";
        }
        catch (OperationCanceledException ex)
        {
            ProvidersStatus = $"Providers load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            ProvidersStatus = $"Providers load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            ProvidersStatus = $"Providers load failed: {ex.Message}";
        }
        finally
        {
            IsProvidersBusy = false;
        }
    }
}

public sealed class ProviderRouteRow
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string Profile { get; init; }
    public required string Circuit { get; init; }
    public required string Usage { get; init; }
    public required string Issues { get; init; }

    public static ProviderRouteRow FromSnapshot(ProviderRouteHealthSnapshot item)
        => new()
        {
            Provider = item.ProviderId,
            Model = item.ModelId,
            Profile = item.ProfileId ?? (item.IsDefaultRoute ? "default" : ""),
            Circuit = item.CircuitState,
            Usage = $"{item.Requests} requests · {item.Errors} errors · {item.Retries} retries",
            Issues = item.ValidationIssues.Length == 0 ? item.LastError ?? "" : string.Join(", ", item.ValidationIssues)
        };
}

public sealed class ToolPresetRow
{
    public required string PresetId { get; init; }
    public required string Surface { get; init; }
    public required string Autonomy { get; init; }
    public required string Approval { get; init; }
    public required string Tools { get; init; }
    public required string Description { get; init; }

    public static ToolPresetRow FromPreset(ResolvedToolPreset item)
        => new()
        {
            PresetId = item.PresetId,
            Surface = item.Surface,
            Autonomy = item.EffectiveAutonomyMode,
            Approval = item.RequireToolApproval ? "approval required" : "no global approval",
            Tools = $"{item.AllowedTools.Count} allowed · {item.ApprovalRequiredTools.Count} approval tools",
            Description = item.Description
        };
}
