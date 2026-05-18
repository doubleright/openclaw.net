using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isWorkflowsBusy;

    [ObservableProperty]
    private string _workflowsStatus = "Workflows not loaded.";

    [ObservableProperty]
    private WorkflowRow? _selectedWorkflow;

    [ObservableProperty]
    private string _workflowInput = "";

    [ObservableProperty]
    private string _workflowRunId = "";

    [ObservableProperty]
    private string _workflowRunDetail = "Run or load a workflow to inspect events and pending inputs.";

    [ObservableProperty]
    private WorkflowPendingInputRow? _selectedWorkflowPendingInput;

    [ObservableProperty]
    private string _workflowResponseJson = "{}";

    public ObservableCollection<WorkflowRow> WorkflowRows { get; } = [];
    public ObservableCollection<WorkflowEventRow> WorkflowEventRows { get; } = [];
    public ObservableCollection<WorkflowPendingInputRow> WorkflowPendingInputs { get; } = [];
    public bool HasWorkflowRows => WorkflowRows.Count > 0;
    public bool HasWorkflowEventRows => WorkflowEventRows.Count > 0;
    public bool HasWorkflowPendingInputs => WorkflowPendingInputs.Count > 0;

    [RelayCommand]
    private async Task LoadWorkflowsAsync()
    {
        if (IsWorkflowsBusy)
            return;

        IsWorkflowsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => WorkflowsStatus = status);
            if (client is null)
                return;

            var response = await client.ListWorkflowsAsync(CancellationToken.None);
            ReplaceItems(WorkflowRows, response.Items.Select(WorkflowRow.FromSummary));
            OnPropertyChanged(nameof(HasWorkflowRows));
            WorkflowsStatus = WorkflowRows.Count == 0 ? "No workflows configured." : $"{WorkflowRows.Count} workflow(s) loaded.";
        }
        catch (OperationCanceledException ex)
        {
            WorkflowsStatus = $"Workflows load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            WorkflowsStatus = $"Workflows load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            WorkflowsStatus = $"Workflows load failed: {ex.Message}";
        }
        finally
        {
            IsWorkflowsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunSelectedWorkflowAsync()
    {
        if (IsWorkflowsBusy)
            return;
        if (SelectedWorkflow is null)
            return;
        if (string.IsNullOrWhiteSpace(WorkflowInput))
        {
            WorkflowsStatus = "Workflow input is required.";
            return;
        }

        var confirmed = await ConfirmMutationAsync(
            "Run workflow",
            $"Run workflow '{SelectedWorkflow.DisplayName}' through the integration API?",
            "Run");
        if (!confirmed)
            return;

        IsWorkflowsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => WorkflowsStatus = status);
            if (client is null)
                return;

            var result = await client.RunWorkflowAsync(SelectedWorkflow.WorkflowId, new AgentWorkflowRequest
            {
                Input = WorkflowInput,
                ChannelId = "companion",
                SenderId = string.IsNullOrWhiteSpace(Username) ? "operator" : Username
            }, CancellationToken.None);

            WorkflowRunId = result.RunId;
            WorkflowRunDetail = BuildWorkflowRunText(result.WorkflowId, result.RunId, result.Status, result.BackendId, result.Output, result.Error);
            ReplaceItems(WorkflowEventRows, result.Events.Select(WorkflowEventRow.FromEvent));
            if (string.Equals(result.Status, AgentWorkflowStatuses.WaitingForInput, StringComparison.OrdinalIgnoreCase))
            {
                var snapshot = await client.GetWorkflowRunAsync(result.WorkflowId, result.RunId, CancellationToken.None);
                ApplyWorkflowSnapshot(snapshot);
            }
            else
            {
                ReplaceItems(WorkflowPendingInputs, []);
                SelectedWorkflowPendingInput = null;
            }
            OnPropertyChanged(nameof(HasWorkflowEventRows));
            OnPropertyChanged(nameof(HasWorkflowPendingInputs));
            WorkflowsStatus = $"Workflow run '{result.RunId}' returned {result.Status}.";
        }
        catch (OperationCanceledException ex)
        {
            WorkflowsStatus = $"Workflow run canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            WorkflowsStatus = $"Workflow run failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            WorkflowsStatus = $"Workflow run failed: {ex.Message}";
        }
        finally
        {
            IsWorkflowsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadWorkflowRunAsync()
    {
        if (IsWorkflowsBusy)
            return;
        if (SelectedWorkflow is null || string.IsNullOrWhiteSpace(WorkflowRunId))
            return;

        IsWorkflowsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => WorkflowsStatus = status);
            if (client is null)
                return;

            var snapshot = await client.GetWorkflowRunAsync(SelectedWorkflow.WorkflowId, WorkflowRunId, CancellationToken.None);
            ApplyWorkflowSnapshot(snapshot);
            WorkflowsStatus = $"Workflow run '{snapshot.RunId}' loaded.";
        }
        catch (OperationCanceledException ex)
        {
            WorkflowsStatus = $"Workflow run load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            WorkflowsStatus = $"Workflow run load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            WorkflowsStatus = $"Workflow run load failed: {ex.Message}";
        }
        finally
        {
            IsWorkflowsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RespondWorkflowInputAsync()
    {
        if (IsWorkflowsBusy)
            return;
        if (SelectedWorkflow is null || SelectedWorkflowPendingInput is null || string.IsNullOrWhiteSpace(WorkflowRunId))
            return;

        var confirmed = await ConfirmMutationAsync(
            "Send workflow response",
            $"Send response to port '{SelectedWorkflowPendingInput.PortId}'?",
            "Send");
        if (!confirmed)
            return;

        var portId = SelectedWorkflowPendingInput.PortId;
        IsWorkflowsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => WorkflowsStatus = status);
            if (client is null)
                return;

            JsonElement? payload = null;
            if (!string.IsNullOrWhiteSpace(WorkflowResponseJson))
            {
                using var doc = JsonDocument.Parse(WorkflowResponseJson);
                payload = doc.RootElement.Clone();
            }

            var snapshot = await client.RespondWorkflowRunAsync(SelectedWorkflow.WorkflowId, WorkflowRunId, new AgentWorkflowResponse
            {
                PortId = portId,
                Payload = payload,
                ActorId = string.IsNullOrWhiteSpace(Username) ? "companion" : Username
            }, CancellationToken.None);
            ApplyWorkflowSnapshot(snapshot);
            WorkflowsStatus = $"Workflow response sent to '{portId}'.";
        }
        catch (OperationCanceledException ex)
        {
            WorkflowsStatus = $"Workflow response canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            WorkflowsStatus = $"Workflow response failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            WorkflowsStatus = $"Workflow response failed: {ex.Message}";
        }
        catch (JsonException ex)
        {
            WorkflowsStatus = $"Workflow response failed: {ex.Message}";
        }
        finally
        {
            IsWorkflowsBusy = false;
        }
    }

    private void ApplyWorkflowSnapshot(AgentWorkflowRunSnapshot snapshot)
    {
        WorkflowRunId = snapshot.RunId;
        WorkflowRunDetail = BuildWorkflowRunText(snapshot.WorkflowId, snapshot.RunId, snapshot.Status, snapshot.BackendId, snapshot.Output, snapshot.Error);
        ReplaceItems(WorkflowEventRows, snapshot.Events.Select(WorkflowEventRow.FromEvent));
        var selectedPortId = SelectedWorkflowPendingInput?.PortId;
        ReplaceItems(WorkflowPendingInputs, snapshot.PendingInputs.Select(WorkflowPendingInputRow.FromInput));
        SelectedWorkflowPendingInput = selectedPortId is null
            ? null
            : WorkflowPendingInputs.FirstOrDefault(input => string.Equals(input.PortId, selectedPortId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasWorkflowEventRows));
        OnPropertyChanged(nameof(HasWorkflowPendingInputs));
    }

    private static string BuildWorkflowRunText(string workflowId, string runId, string status, string? backendId, string? output, string? error)
        => string.Join(Environment.NewLine, new[]
        {
            $"Workflow: {workflowId}",
            $"Run: {runId}",
            $"Status: {status}",
            $"Backend: {backendId ?? "default"}",
            $"Output: {output ?? ""}",
            $"Error: {error ?? ""}"
        });
}

public sealed class WorkflowRow
{
    public required string WorkflowId { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
    public required string WorkflowName { get; init; }
    public required string Status { get; init; }

    public static WorkflowRow FromSummary(AgentWorkflowBackendSummary item)
        => new()
        {
            WorkflowId = item.Id,
            DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName,
            Kind = item.Kind,
            WorkflowName = item.WorkflowName,
            Status = item.Enabled ? "enabled" : "disabled"
        };
}

public sealed class WorkflowEventRow
{
    public required string Timestamp { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }

    public static WorkflowEventRow FromEvent(AgentWorkflowEvent item)
        => new()
        {
            Timestamp = item.TimestampUtc.ToLocalTime().ToString("g"),
            Type = item.Type,
            Status = item.Status ?? "",
            Summary = item.Summary
        };
}

public sealed class WorkflowPendingInputRow
{
    public required string PortId { get; init; }
    public required string Summary { get; init; }
    public required string PayloadPreview { get; init; }

    public static WorkflowPendingInputRow FromInput(AgentWorkflowPendingInput item)
        => new()
        {
            PortId = item.PortId,
            Summary = item.Summary ?? "",
            PayloadPreview = item.Payload?.GetRawText() ?? ""
        };
}
