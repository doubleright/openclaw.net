using System.Text.Json;
using OpenClaw.Core.Models;

namespace OpenClaw.Core.Canvas;

public static class A2UiV09MessageValidator
{
    private static readonly HashSet<string> SupportedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "createSurface",
        "updateComponents",
        "updateDataModel",
        "deleteSurface",
        "syncUIToData",
        "action",
        "error"
    };

    public static A2UiValidationResult Validate(WsServerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(envelope.Operation))
            return A2UiValidationResult.Invalid("A2UI v0.9 operation is required.");

        if (!SupportedOperations.Contains(envelope.Operation))
            return A2UiValidationResult.Invalid($"A2UI v0.9 operation '{envelope.Operation}' is not supported.");

        return envelope.Operation.ToLowerInvariant() switch
        {
            "createsurface" => ValidateCreateSurface(envelope),
            "updatecomponents" => ValidateUpdateComponents(envelope),
            "updatedatamodel" => ValidateUpdateDataModel(envelope),
            "deletesurface" => ValidateSurfaceOperation(envelope, "deleteSurface"),
            "syncuitodata" => ValidateSyncUIToData(envelope),
            "action" => ValidateAction(envelope),
            "error" => ValidateError(envelope),
            _ => A2UiValidationResult.Invalid($"A2UI v0.9 operation '{envelope.Operation}' is not supported.")
        };
    }

    private static A2UiValidationResult ValidateCreateSurface(WsServerEnvelope envelope)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        if (surfaceError is not null)
            return A2UiValidationResult.Invalid(surfaceError);

        if (!A2UiCatalogRegistry.TryChooseCatalog(envelope.SupportedCatalogIds, envelope.CatalogId, out var catalog))
            return A2UiValidationResult.Invalid("A2UI v0.9 createSurface uses an unsupported catalog ID.");

        if (envelope.Components is { } components)
        {
            var componentValidation = ValidateComponents(components, catalog, "createSurface");
            if (!componentValidation.IsValid)
                return componentValidation;
        }

        return ValidateOptionalJsonObject(envelope.DataModelJson, "dataModelJson")
            ?? A2UiValidationResult.Valid(1);
    }

    private static A2UiValidationResult ValidateUpdateComponents(WsServerEnvelope envelope)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        if (surfaceError is not null)
            return A2UiValidationResult.Invalid(surfaceError);

        if (!A2UiCatalogRegistry.TryChooseCatalog(envelope.SupportedCatalogIds, envelope.CatalogId, out var catalog))
            return A2UiValidationResult.Invalid("A2UI v0.9 updateComponents uses an unsupported catalog ID.");

        if (envelope.Components is null || envelope.Components.Length == 0)
            return A2UiValidationResult.Invalid("A2UI v0.9 updateComponents requires components as a non-empty JSON string array.");

        return ValidateComponents(envelope.Components, catalog, "updateComponents");
    }

    private static A2UiValidationResult ValidateComponents(string[] components, A2UiCatalogDescriptor catalog, string operation)
    {
        if (components.Length == 0)
            return A2UiValidationResult.Invalid($"A2UI v0.9 {operation} requires components as a non-empty JSON string array.");

        for (var i = 0; i < components.Length; i++)
        {
            var index = i + 1;
            var componentJson = components[i];
            if (string.IsNullOrWhiteSpace(componentJson))
                return A2UiValidationResult.Invalid($"A2UI v0.9 component {index} must be a non-empty JSON string.");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(componentJson);
            }
            catch (JsonException ex)
            {
                return A2UiValidationResult.Invalid($"A2UI v0.9 component {index} is not valid JSON: {ex.Message}");
            }

            using (doc)
            {
                var component = doc.RootElement;
                if (component.ValueKind != JsonValueKind.Object)
                    return A2UiValidationResult.Invalid($"A2UI v0.9 component {index} must be a JSON object.");

                if (!component.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                    return A2UiValidationResult.Invalid($"A2UI v0.9 component {index} is missing string property 'type'.");

                var type = typeProp.GetString();
                if (!A2UiCatalogRegistry.IsSupportedComponentType(catalog, type))
                    return A2UiValidationResult.Invalid($"A2UI v0.9 component {index} has unsupported component type '{type}'.");
            }
        }

        return A2UiValidationResult.Valid(1);
    }

    private static A2UiValidationResult ValidateUpdateDataModel(WsServerEnvelope envelope)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        if (surfaceError is not null)
            return A2UiValidationResult.Invalid(surfaceError);

        if (string.IsNullOrWhiteSpace(envelope.DataModelJson))
            return A2UiValidationResult.Invalid("A2UI v0.9 updateDataModel requires dataModelJson.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(envelope.DataModelJson);
        }
        catch (JsonException ex)
        {
            return A2UiValidationResult.Invalid($"A2UI v0.9 dataModelJson is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? A2UiValidationResult.Valid(1)
                : A2UiValidationResult.Invalid("A2UI v0.9 dataModelJson must be a JSON object.");
        }
    }

    private static A2UiValidationResult ValidateSurfaceOperation(WsServerEnvelope envelope, string operation)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        return surfaceError is null
            ? A2UiValidationResult.Valid(1)
            : A2UiValidationResult.Invalid($"A2UI v0.9 {operation} {surfaceError}");
    }

    private static A2UiValidationResult ValidateSyncUIToData(WsServerEnvelope envelope)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        if (surfaceError is not null)
            return A2UiValidationResult.Invalid($"A2UI v0.9 syncUIToData {surfaceError}");

        return ValidateOptionalJsonObject(envelope.DataModelJson, "dataModelJson")
            ?? A2UiValidationResult.Valid(1);
    }

    private static A2UiValidationResult ValidateAction(WsServerEnvelope envelope)
    {
        var surfaceError = ValidateSurfaceId(envelope);
        if (surfaceError is not null)
            return A2UiValidationResult.Invalid($"A2UI v0.9 action {surfaceError}");

        if (string.IsNullOrWhiteSpace(envelope.Action))
            return A2UiValidationResult.Invalid("A2UI v0.9 action requires action.");

        return ValidateOptionalJsonObject(envelope.ParametersJson, "parametersJson")
            ?? A2UiValidationResult.Valid(1);
    }

    private static A2UiValidationResult ValidateError(WsServerEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Error) && string.IsNullOrWhiteSpace(envelope.DiagnosticCode))
            return A2UiValidationResult.Invalid("A2UI v0.9 error requires error or diagnosticCode.");

        return A2UiValidationResult.Valid(1);
    }

    private static A2UiValidationResult? ValidateOptionalJsonObject(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return A2UiValidationResult.Invalid($"A2UI v0.9 {propertyName} is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? null
                : A2UiValidationResult.Invalid($"A2UI v0.9 {propertyName} must be a JSON object.");
        }
    }

    private static string? ValidateSurfaceId(WsServerEnvelope envelope)
        => string.IsNullOrWhiteSpace(envelope.SurfaceId)
            ? "requires non-empty surfaceId."
            : null;
}
