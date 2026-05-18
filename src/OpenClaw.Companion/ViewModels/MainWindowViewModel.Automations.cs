using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isAutomationsBusy;

    [ObservableProperty]
    private string _automationsStatus = "Automations not loaded.";

    [ObservableProperty]
    private AutomationRow? _selectedAutomation;

    [ObservableProperty]
    private AutomationRunRow? _selectedAutomationRun;

    [ObservableProperty]
    private string _automationDetail = "Select an automation to inspect run state.";

    public ObservableCollection<AutomationRow> AutomationRows { get; } = [];
    public ObservableCollection<AutomationTemplateRow> AutomationTemplateRows { get; } = [];
    public ObservableCollection<AutomationRunRow> AutomationRunRows { get; } = [];
    public bool HasAutomationRows => AutomationRows.Count > 0;
    public bool HasAutomationTemplateRows => AutomationTemplateRows.Count > 0;
    public bool HasAutomationRunRows => AutomationRunRows.Count > 0;

    partial void OnSelectedAutomationChanged(AutomationRow? value)
    {
        if (value is null)
        {
            AutomationDetail = "Select an automation to inspect run state.";
            ReplaceItems(AutomationRunRows, []);
            OnPropertyChanged(nameof(HasAutomationRunRows));
            return;
        }

        _ = LoadAutomationDetailAsync(value.AutomationId);
    }

    [RelayCommand]
    private async Task LoadAutomationsAsync()
    {
        if (IsAutomationsBusy)
            return;

        IsAutomationsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => AutomationsStatus = status);
            if (client is null)
                return;

            var automations = await client.ListAutomationsAsync(CancellationToken.None);
            var templates = await client.ListAutomationTemplatesAsync(CancellationToken.None);
            ReplaceItems(AutomationRows, automations.Items.Select(AutomationRow.FromDefinition));
            ReplaceItems(AutomationTemplateRows, templates.Items.Select(AutomationTemplateRow.FromTemplate));
            OnPropertyChanged(nameof(HasAutomationRows));
            OnPropertyChanged(nameof(HasAutomationTemplateRows));
            AutomationsStatus = $"Loaded {AutomationRows.Count} automation(s) and {AutomationTemplateRows.Count} template(s).";
        }
        catch (OperationCanceledException ex)
        {
            AutomationsStatus = $"Automations load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            AutomationsStatus = $"Automations load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            AutomationsStatus = $"Automations load failed: {ex.Message}";
        }
        finally
        {
            IsAutomationsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunSelectedAutomationDryRunAsync()
    {
        if (SelectedAutomation is null)
            return;

        await RunAutomationCoreAsync(SelectedAutomation.AutomationId, dryRun: true);
    }

    [RelayCommand]
    private async Task RunSelectedAutomationLiveAsync()
    {
        if (SelectedAutomation is null)
            return;

        var confirmed = await ConfirmMutationAsync(
            "Run automation",
            $"Run automation '{SelectedAutomation.Name}' live? This can enqueue runtime work and delivery.",
            "Run live");
        if (!confirmed)
            return;

        await RunAutomationCoreAsync(SelectedAutomation.AutomationId, dryRun: false);
    }

    [RelayCommand]
    private async Task DeleteSelectedAutomationAsync()
    {
        if (SelectedAutomation is null)
            return;

        var confirmed = await ConfirmMutationAsync(
            "Delete automation",
            $"Delete automation '{SelectedAutomation.Name}'? This cannot be undone from the Companion.",
            "Delete");
        if (!confirmed)
            return;

        try
        {
            using var client = RequireIntegrationClient(status => AutomationsStatus = status);
            if (client is null)
                return;

            var result = await client.DeleteAutomationAsync(SelectedAutomation.AutomationId, CancellationToken.None);
            var mutationStatus = result.Success ? result.Message : result.Error ?? "Automation delete failed.";
            if (result.Success)
                await LoadAutomationsAsync();
            AutomationsStatus = mutationStatus;
        }
        catch (OperationCanceledException ex)
        {
            AutomationsStatus = $"Automation delete canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            AutomationsStatus = $"Automation delete failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            AutomationsStatus = $"Automation delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReplaySelectedAutomationRunAsync()
    {
        if (SelectedAutomation is null || SelectedAutomationRun is null)
            return;

        var confirmed = await ConfirmMutationAsync(
            "Replay automation run",
            $"Replay run '{SelectedAutomationRun.RunId}' for automation '{SelectedAutomation.Name}'?",
            "Replay");
        if (!confirmed)
            return;

        try
        {
            using var client = RequireIntegrationClient(status => AutomationsStatus = status);
            if (client is null)
                return;

            var result = await client.ReplayAutomationRunAsync(SelectedAutomation.AutomationId, SelectedAutomationRun.RunId, CancellationToken.None);
            AutomationsStatus = result.Success ? result.Message : result.Error ?? "Automation replay failed.";
            await LoadAutomationDetailAsync(SelectedAutomation.AutomationId);
        }
        catch (OperationCanceledException ex)
        {
            AutomationsStatus = $"Automation replay canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            AutomationsStatus = $"Automation replay failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            AutomationsStatus = $"Automation replay failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearSelectedAutomationQuarantineAsync()
    {
        if (SelectedAutomation is null)
            return;

        var confirmed = await ConfirmMutationAsync(
            "Clear quarantine",
            $"Clear quarantine for automation '{SelectedAutomation.Name}'?",
            "Clear");
        if (!confirmed)
            return;

        try
        {
            using var client = RequireIntegrationClient(status => AutomationsStatus = status);
            if (client is null)
                return;

            var result = await client.ClearAutomationQuarantineAsync(SelectedAutomation.AutomationId, CancellationToken.None);
            AutomationsStatus = result.Success ? result.Message : result.Error ?? "Clear quarantine failed.";
            await LoadAutomationDetailAsync(SelectedAutomation.AutomationId);
        }
        catch (OperationCanceledException ex)
        {
            AutomationsStatus = $"Clear quarantine canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            AutomationsStatus = $"Clear quarantine failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            AutomationsStatus = $"Clear quarantine failed: {ex.Message}";
        }
    }

    private async Task RunAutomationCoreAsync(string automationId, bool dryRun)
    {
        try
        {
            using var client = RequireIntegrationClient(status => AutomationsStatus = status);
            if (client is null)
                return;

            var result = await client.RunAutomationAsync(automationId, dryRun, CancellationToken.None);
            AutomationsStatus = result.Success ? result.Message : result.Error ?? "Automation run failed.";
            await LoadAutomationDetailAsync(automationId);
        }
        catch (OperationCanceledException ex)
        {
            AutomationsStatus = $"Automation run canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            AutomationsStatus = $"Automation run failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            AutomationsStatus = $"Automation run failed: {ex.Message}";
        }
    }

    private async Task LoadAutomationDetailAsync(string automationId)
    {
        try
        {
            using var client = RequireIntegrationClient(status => AutomationDetail = status);
            if (client is null)
                return;

            var detail = await client.GetAutomationAsync(automationId, CancellationToken.None);
            var runs = await client.GetAutomationRunsAsync(automationId, CancellationToken.None);
            if (SelectedAutomation?.AutomationId != automationId)
                return;

            AutomationDetail = detail.Automation is null
                ? "Automation not found."
                : string.Join(Environment.NewLine, new[]
                {
                    $"Name: {detail.Automation.Name}",
                    $"Schedule: {detail.Automation.Schedule}",
                    $"Enabled: {detail.Automation.Enabled}",
                    $"Delivery: {detail.Automation.DeliveryChannelId}",
                    $"Health: {detail.RunState?.HealthState ?? "unknown"}",
                    $"Lifecycle: {detail.RunState?.LifecycleState ?? "never"}",
                    $"Last run: {FormatTimestamp(detail.RunState?.LastRunAtUtc)}",
                    $"Next retry: {FormatTimestamp(detail.RunState?.NextRetryAtUtc)}",
                    $"Quarantine: {detail.RunState?.QuarantineReason ?? "none"}"
                });
            ReplaceItems(AutomationRunRows, runs.Items.Select(AutomationRunRow.FromRecord));
            OnPropertyChanged(nameof(HasAutomationRunRows));
        }
        catch (OperationCanceledException ex) when (SelectedAutomation?.AutomationId == automationId)
        {
            AutomationDetail = $"Automation detail load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex) when (SelectedAutomation?.AutomationId == automationId)
        {
            AutomationDetail = $"Automation detail load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex) when (SelectedAutomation?.AutomationId == automationId)
        {
            AutomationDetail = $"Automation detail load failed: {ex.Message}";
        }
    }
}

public sealed class AutomationRow
{
    public required string AutomationId { get; init; }
    public required string Name { get; init; }
    public required string Schedule { get; init; }
    public required string Delivery { get; init; }
    public required string State { get; init; }
    public required string Tags { get; init; }

    public static AutomationRow FromDefinition(AutomationDefinition item)
        => new()
        {
            AutomationId = item.Id,
            Name = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
            Schedule = item.Schedule,
            Delivery = item.DeliveryChannelId,
            State = item.IsDraft ? "draft" : item.Enabled ? "enabled" : "disabled",
            Tags = string.Join(", ", item.Tags)
        };
}

public sealed class AutomationTemplateRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string Availability { get; init; }

    public static AutomationTemplateRow FromTemplate(AutomationTemplate item)
        => new()
        {
            Key = item.Key,
            Label = item.Label,
            Category = item.Category,
            Description = item.Description,
            Availability = item.Available ? "available" : item.Reason ?? "unavailable"
        };
}

public sealed class AutomationRunRow
{
    public required string RunId { get; init; }
    public required string Trigger { get; init; }
    public required string Lifecycle { get; init; }
    public required string Verification { get; init; }
    public required string Started { get; init; }
    public required string Summary { get; init; }

    public static AutomationRunRow FromRecord(AutomationRunRecord item)
        => new()
        {
            RunId = item.RunId,
            Trigger = item.TriggerSource,
            Lifecycle = item.LifecycleState,
            Verification = item.VerificationStatus,
            Started = item.StartedAtUtc.ToLocalTime().ToString("g"),
            Summary = item.VerificationSummary ?? item.MessagePreview ?? ""
        };
}
