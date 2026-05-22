namespace OpenClaw.Dashboard.Models;

public record PluginInfo(
    string Id,
    string? Name,
    string? Description,
    bool Enabled,
    string? Version
);

public record ApprovalPolicy(
    string? Id,
    string? ToolPattern,
    string? Policy,
    string? Description
);

public record DeadLetterItem(
    string? Id,
    string? WebhookUrl,
    string? Error,
    DateTime? FailedAt,
    int RetryCount
);
