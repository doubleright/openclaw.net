using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace OpenClaw.Dashboard.Services;

/// <summary>
/// Lightweight, NativeAOT-friendly i18n service for the Blazor WASM dashboard.
/// Loads JSON resources from <c>wwwroot/locales/{locale}.json</c> and flattens
/// nested objects to dotted keys (e.g. <c>"common.save"</c>).
/// </summary>
public class LocalizationService
{
    private const string DefaultLocale = "en-US";
    private const string FallbackLocale = "en-US";

    private static readonly string[] SupportedLocales = { "en-US", "zh-CN" };

    private readonly HttpClient _http;
    private readonly SettingsService _settings;

    private Dictionary<string, string> _translations = new(StringComparer.Ordinal);
    private string _currentLocale = DefaultLocale;
    private bool _initialized;

    public LocalizationService(HttpClient http, SettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    /// <summary>Currently active locale (e.g. <c>en-US</c>).</summary>
    public string CurrentLocale => _currentLocale;

    /// <summary>Locales advertised to UI selectors.</summary>
    public IReadOnlyList<string> AvailableLocales => SupportedLocales;

    /// <summary>Raised whenever the active language changes and translations are reloaded.</summary>
    public event Action? OnLanguageChanged;

    /// <summary>
    /// Resolve preferred locale (localStorage → browser → default), then load translations.
    /// Must be invoked from a component lifecycle method (e.g. <c>OnInitializedAsync</c>).
    /// </summary>
    public async Task InitializeAsync(IJSRuntime js)
    {
        if (_initialized)
        {
            return;
        }

        var locale = await _settings.GetLocaleAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(locale))
        {
            locale = await DetectBrowserLocaleAsync(js).ConfigureAwait(false);
        }

        locale = NormalizeLocale(locale);

        await LoadTranslationsAsync(locale).ConfigureAwait(false);
        _currentLocale = locale;
        _initialized = true;
        OnLanguageChanged?.Invoke();
    }

    /// <summary>
    /// Switch active language, persist to localStorage and notify subscribers.
    /// </summary>
    public async Task SetLanguageAsync(string locale)
    {
        locale = NormalizeLocale(locale);
        if (string.Equals(locale, _currentLocale, StringComparison.Ordinal) && _initialized)
        {
            return;
        }

        await LoadTranslationsAsync(locale).ConfigureAwait(false);
        _currentLocale = locale;
        await _settings.SetLocaleAsync(locale).ConfigureAwait(false);
        OnLanguageChanged?.Invoke();
    }

    /// <summary>Lookup a translation by dotted key. Returns the key itself if missing.</summary>
    public string T(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return _translations.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>Lookup a translation and apply <see cref="string.Format(string, object?[])"/>.</summary>
    public string T(string key, params object[] args)
    {
        var template = T(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>Indexer convenience for Razor: <c>@L["common.save"]</c>.</summary>
    public string this[string key] => T(key);

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return DefaultLocale;
        }

        foreach (var supported in SupportedLocales)
        {
            if (string.Equals(supported, locale, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        // Map by language prefix.
        if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return DefaultLocale;
    }

    private static async Task<string> DetectBrowserLocaleAsync(IJSRuntime js)
    {
        try
        {
            var navLang = await js.InvokeAsync<string?>("openclawDashboard.getBrowserLanguage")
                .ConfigureAwait(false);
            return string.IsNullOrEmpty(navLang) ? DefaultLocale : navLang!;
        }
        catch
        {
            return DefaultLocale;
        }
    }

    private async Task LoadTranslationsAsync(string locale)
    {
        var loaded = await FetchLocaleAsync(locale).ConfigureAwait(false);

        if (loaded is null && !string.Equals(locale, FallbackLocale, StringComparison.Ordinal))
        {
            loaded = await FetchLocaleAsync(FallbackLocale).ConfigureAwait(false);
        }

        _translations = loaded ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>?> FetchLocaleAsync(string locale)
    {
        try
        {
            using var stream = await _http.GetStreamAsync($"locales/{locale}.json").ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenJson(doc.RootElement, prefix: string.Empty, result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? property.Name
                        : string.Concat(prefix, ".", property.Name);
                    FlattenJson(property.Value, key, result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var key = string.Concat(prefix, ".", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    FlattenJson(item, key, result);
                    index++;
                }
                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.GetRawText();
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                result[prefix] = string.Empty;
                break;
        }
    }
}
