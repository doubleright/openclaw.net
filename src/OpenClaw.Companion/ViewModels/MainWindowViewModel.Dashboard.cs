using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isDashboardBusy;

    [ObservableProperty]
    private string _dashboardStatus = "Dashboard not loaded.";

    [ObservableProperty]
    private int _dashboardActiveSessions;

    [ObservableProperty]
    private int _dashboardPendingApprovals;

    [ObservableProperty]
    private int _dashboardProviders;

    [ObservableProperty]
    private int _dashboardPlugins;

    [ObservableProperty]
    private int _dashboardAutomations;

    [ObservableProperty]
    private int _dashboardChannelsReady;

    [ObservableProperty]
    private string _dashboardLastRefreshed = "never";

    public string DashboardLocalGatewayStatus => LocalGatewayIsHealthy ? "Local gateway healthy" : LocalGatewayStatus;
    public bool HasDashboardApprovalHistory => DashboardApprovalHistory.Count > 0;
    public bool HasDashboardEvents => DashboardRecentEvents.Count > 0;
    public bool HasDashboardChannels => DashboardChannels.Count > 0;

    public ObservableCollection<ApprovalHistoryItem> DashboardApprovalHistory { get; } = [];
    public ObservableCollection<RuntimeEventRow> DashboardRecentEvents { get; } = [];
    public ObservableCollection<ChannelReadinessRow> DashboardChannels { get; } = [];

    [RelayCommand]
    private async Task LoadDashboardAsync()
    {
        if (IsDashboardBusy)
            return;

        IsDashboardBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => DashboardStatus = status);
            if (client is null)
                return;

            var response = await client.GetIntegrationDashboardAsync(CancellationToken.None);
            DashboardActiveSessions = response.Status.ActiveSessions;
            DashboardPendingApprovals = response.Status.PendingApprovals;
            DashboardProviders = response.Providers.Routes.Count;
            DashboardPlugins = response.Plugins.Items.Count;
            DashboardAutomations = response.Operator.Automations.Total;
            DashboardChannelsReady = response.Operator.Channels.Ready;
            DashboardLastRefreshed = DateTimeOffset.Now.ToString("g");
            DashboardStatus = "Runtime dashboard loaded.";

            ReplaceItems(DashboardApprovalHistory, response.ApprovalHistory.Items.Take(8).Select(ApprovalHistoryItem.FromEntry));
            ReplaceItems(DashboardRecentEvents, response.Events.Items.Take(8).Select(RuntimeEventRow.FromEntry));
            ReplaceItems(DashboardChannels, response.Operator.Channels.Items.Select(ChannelReadinessRow.FromDto));
            OnPropertyChanged(nameof(HasDashboardApprovalHistory));
            OnPropertyChanged(nameof(HasDashboardEvents));
            OnPropertyChanged(nameof(HasDashboardChannels));
        }
        catch (OperationCanceledException ex)
        {
            DashboardStatus = $"Dashboard load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            DashboardStatus = $"Dashboard load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            DashboardStatus = $"Dashboard load failed: {ex.Message}";
        }
        finally
        {
            IsDashboardBusy = false;
        }
    }
}

public sealed class ChannelReadinessRow
{
    public required string ChannelId { get; init; }
    public required string DisplayName { get; init; }
    public required string Mode { get; init; }
    public required string Status { get; init; }
    public required string Detail { get; init; }
    public bool Enabled { get; init; }
    public bool Ready { get; init; }

    public static ChannelReadinessRow FromDto(ChannelReadinessDto item)
        => new()
        {
            ChannelId = item.ChannelId,
            DisplayName = item.DisplayName,
            Mode = item.Mode,
            Status = item.Status,
            Enabled = item.Enabled,
            Ready = item.Ready,
            Detail = BuildDetail(item)
        };

    private static string BuildDetail(ChannelReadinessDto item)
    {
        var missing = item.MissingRequirements.Count == 0 ? "" : $"Missing: {string.Join(", ", item.MissingRequirements)}";
        var warnings = item.Warnings.Count == 0 ? "" : $"Warnings: {string.Join(", ", item.Warnings)}";
        return string.Join(" · ", new[] { missing, warnings }.Where(static text => text.Length > 0));
    }
}
