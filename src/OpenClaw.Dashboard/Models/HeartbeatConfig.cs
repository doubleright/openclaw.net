namespace OpenClaw.Dashboard.Models;

public record HeartbeatConfig(
    bool Enabled,
    string? CronExpression,
    List<HeartbeatTask>? Tasks
);

public record HeartbeatTask(
    string? Name,
    string? Prompt,
    string? Template,
    Dictionary<string, string>? Parameters
);

public record HeartbeatStatus(
    bool Running,
    HeartbeatRunStatus? LastRun,
    DateTime? NextRun,
    string? LastResult
);

public record HeartbeatRunStatus(
    string? Outcome,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? LastDeliveredAtUtc,
    bool DeliverySuppressed,
    long InputTokens,
    long OutputTokens,
    string? SessionId,
    string? MessagePreview
);

public record PulseStatus(
    bool Running,
    string? State,
    List<PulseEvent>? RecentEvents
);

public record PulseEvent(
    string? EventType,
    DateTime? Timestamp,
    string? Details
);

public record PulseEventListResponse(
    List<PulseEvent> Items
);
