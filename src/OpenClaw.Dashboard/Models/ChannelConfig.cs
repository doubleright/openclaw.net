namespace OpenClaw.Dashboard.Models;

public record ChannelInfo(
    string? Id,
    string? Type,
    string? Status,
    bool Enabled,
    Dictionary<string, object>? Config
);

public record AllowlistEntry(
    string? Channel,
    string? SenderId,
    string? Label
);
