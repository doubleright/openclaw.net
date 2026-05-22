using System.Text.Json;
using Microsoft.JSInterop;

namespace OpenClaw.Dashboard.Services;

public record DashboardSettings(string? ProxyApiBaseUrl, string? ApiKey);

public class SettingsService
{
    private const string SettingsKey = "openclaw.dashboard.settings.v1";
    private const string LocaleKey = "openclaw.dashboard.locale";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IJSRuntime _js;

    public SettingsService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<DashboardSettings> LoadAsync()
    {
        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", SettingsKey)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(raw))
            {
                return new DashboardSettings(null, null);
            }

            var settings = JsonSerializer.Deserialize<DashboardSettings>(raw, JsonOptions);
            return settings ?? new DashboardSettings(null, null);
        }
        catch
        {
            return new DashboardSettings(null, null);
        }
    }

    public async Task SaveAsync(DashboardSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await _js.InvokeVoidAsync("localStorage.setItem", SettingsKey, json)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore localStorage failures
        }
    }

    public async Task<string?> GetLocaleAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("localStorage.getItem", LocaleKey)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetLocaleAsync(string locale)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", LocaleKey, locale)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }
}
