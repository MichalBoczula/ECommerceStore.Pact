using System.Text;
using System.Text.Json;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace ECommerceStore.Pact.Infrastructure.Contracts;

public sealed class ContractDocumentValidator : IContractDocumentValidator
{
    public async Task<string> ValidateAsync(ContractKind kind, byte[] content, IntegrationIdentity? integration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonDocument document;
        string json;
        try
        {
            json = new UTF8Encoding(false, true).GetString(content);
            if (json.StartsWith('\uFEFF')) json = json[1..];
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw Invalid("Upload a UTF-8 JSON document with a maximum nesting depth of 64.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("The contract root must be an object.");
            CheckProperties(root, kind == ContractKind.ProviderOpenApi);
            if (kind == ContractKind.ConsumerPact) return ValidatePact(root, integration);
            if (root.TryGetProperty("interactions", out _) || root.TryGetProperty("swagger", out _))
                throw Invalid("Use an OpenAPI 3.0/3.1 JSON document for ProviderOpenApi.");
            var specification = Text(root, "openapi");
            if (!Version.TryParse(specification, out var version) || version.Major != 3 || version.Minor is not (0 or 1))
                throw Invalid("Only OpenAPI 3.0.x and 3.1.x JSON documents are supported.");
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var parsed = await OpenApiDocument.LoadAsync(stream, "json", new OpenApiReaderSettings
                {
                    LoadExternalRefs = false,
                    RuleSet = ValidationRuleSet.GetDefaultRuleSet()
                }, cancellationToken);
                if (parsed.Document is null || parsed.Diagnostic is { Errors.Count: > 0 })
                    throw Invalid("The OpenAPI document failed the pinned OpenAPI reader's validation.");
            }
            catch (Exception exception) when (exception is OpenApiException or FormatException or JsonException)
            {
                throw Invalid("The OpenAPI document could not be parsed.");
            }
            return specification;
        }
    }

    private static string ValidatePact(JsonElement root, IntegrationIdentity? integration)
    {
        if (integration is null || root.TryGetProperty("openapi", out _) || root.TryGetProperty("messages", out _))
            throw Invalid("ConsumerPact requires a declared integration and HTTP interactions only.");
        if (Text(Object(root, "consumer"), "name") != integration.ConsumerApplicationId ||
            Text(Object(root, "provider"), "name") != integration.ProviderApplicationId)
            throw Invalid("Pact party names must exactly match the registered consumer/provider application IDs.");
        var metadata = Object(root, "metadata");
        var specification = Text(Object(metadata, "pactSpecification"), "version");
        if (!Version.TryParse(specification, out var version) || version.Major is not (3 or 4) || version.Minor != 0 || version.Build > 0 || version.Revision > 0)
            throw Invalid("Only Pact specification 3.0 and 4.0 HTTP documents are supported.");
        if (metadata.TryGetProperty("plugins", out var plugins) && (plugins.ValueKind != JsonValueKind.Array || plugins.GetArrayLength() != 0))
            throw Invalid("Pact plugins are not supported by this upload profile.");
        if (!root.TryGetProperty("interactions", out var interactions) || interactions.ValueKind != JsonValueKind.Array ||
            interactions.GetArrayLength() is 0 or > 1000)
            throw Invalid("A Pact must contain 1 to 1000 HTTP interactions.");
        foreach (var interaction in interactions.EnumerateArray())
        {
            _ = Text(interaction, "description");
            if (version.Major == 4 && Text(interaction, "type") != "Synchronous/HTTP")
                throw Invalid("Only Synchronous/HTTP Pact v4 interactions are supported.");
            var request = Object(interaction, "request");
            var method = Text(request, "method");
            if (method.Length > 20 || !method.All(char.IsAsciiLetter) || !Text(request, "path").StartsWith('/'))
                throw Invalid("HTTP interactions require a method and an absolute request path.");
            var response = Object(interaction, "response");
            if (!response.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var number) || number is < 100 or > 599)
                throw Invalid("HTTP interactions require a numeric status between 100 and 599.");
        }
        // Shape/profile validation only. Pact's matching rules and generators remain opaque;
        // official Pact tooling executes and verifies them in consumer/provider CI.
        return $"{version.Major}.0.0";
    }

    private static void CheckProperties(JsonElement value, bool forbidExternalReferences)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("Duplicate JSON property names are not supported.");
                if (forbidExternalReferences && property.Name == "$ref" && property.Value.ValueKind == JsonValueKind.String &&
                    !(property.Value.GetString() ?? "").StartsWith('#'))
                    throw Invalid("Upload a self-contained JSON contract; external references are not loaded.");
                CheckProperties(property.Value, forbidExternalReferences);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) CheckProperties(child, forbidExternalReferences);
    }

    private static JsonElement Object(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object
            ? child : throw Invalid($"An object named {name} is required.");
    private static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(child.GetString()) ? child.GetString()! : throw Invalid($"A string named {name} is required.");
    private static RegistryValidationException Invalid(string message) => new("contentBase64", message);
}
