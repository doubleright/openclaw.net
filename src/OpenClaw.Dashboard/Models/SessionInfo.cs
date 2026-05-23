namespace OpenClaw.Dashboard.Models;

public record SessionInfo(
    string SessionId,
    string? ChannelId,
    string? SenderId,
    DateTime? LastActive,
    Dictionary<string, object>? Metadata
);

public record SessionDetail(
    string SessionId,
    string? ChannelId,
    string? SenderId,
    DateTime? LastActive,
    Dictionary<string, object>? Metadata,
    List<SessionMessage>? Messages
);

public record SessionMessage(
    string Role,
    string? Content,
    DateTime? Timestamp
);
