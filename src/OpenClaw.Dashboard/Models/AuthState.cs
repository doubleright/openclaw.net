namespace OpenClaw.Dashboard.Models;

public record AuthState(
    string? Username,
    string? DisplayName,
    string Role,
    bool IsBootstrapAdmin,
    string AuthMode,
    string? CsrfToken = null
);
