namespace OpenClaw.Dashboard.Models;

public record AutomationConfig(
    string? Id,
    string? Name,
    string? Schedule,
    string? Prompt,
    string? TemplateKey,
    bool Enabled
);

public record AutomationListResponse(
    List<AutomationConfig> Items
);

public record AutomationPreview(
    string? PromptPreview,
    int EstimatedRunsPerMonth,
    List<AutomationValidationIssue>? Issues
);

public record AutomationValidationIssue(
    string? Severity,
    string? Code,
    string? Message
);
