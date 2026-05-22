namespace OpenClaw.Dashboard.Models;

public record WhatsAppSetup(
    bool Enabled,
    string? Type,
    string? WebhookPath,
    string? PhoneNumberId,
    string? BusinessAccountId,
    string? AccessToken,
    string? VerifyToken
);

public record WhatsAppAuthState(
    string? ChannelId,
    string? AccountId,
    string? State,
    string? QrCodeUrl
);
