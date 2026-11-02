using System.Security.Cryptography;
using System.Text.Json;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;

namespace ECommerceStore.Pact.Application.History;

public sealed class ContractComparisonService(ContractPublicationService publications, IIntegrationRepository integrations,
    IContractComparisonRepository comparisons, IContractSchemaComparer comparer, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<ContractComparison>> CompareAsync(CompareContractsCommand command, string publisherId, CancellationToken cancellationToken)
    {
        var integration = new IntegrationIdentity(command.ConsumerApplicationId, command.ProviderApplicationId);
        var owner = RegistryIdentity.ApplicationId(command.SourceApplicationId, "sourceApplicationId");
        var before = await publications.GetPublicationAsync(command.BeforePublicationId, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("baseline_publication_not_found");
        var after = await publications.GetPublicationAsync(command.AfterPublicationId, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("candidate_publication_not_found");
        if (before.Publication.Provenance.SourceRef != command.BaselineRef || before.Binding.Kind != after.Binding.Kind ||
            new[] { before, after }.Any(value => value.Binding.Source.ApplicationId != owner ||
                (value.Binding.Kind == ContractKind.ConsumerPact ? value.Binding.Integration != integration : owner != integration.ProviderApplicationId)))
            throw new RegistryValidationException("comparison", "Select same-kind publications owned by the source application within this integration, and the baseline publication's exact ref.");
        if (await integrations.GetAsync(integration, cancellationToken) is null) throw new RegistryReferenceNotFoundException("integration_not_found");
        var key = ContractComparison.Identity(integration, before.Publication.Id, after.Publication.Id, command.BaselineRef);
        var existing = await comparisons.GetByKeyAsync(key, cancellationToken);
        if (existing is not null) return new(existing, false);
        var analysis = before.Artifact.Kind == ContractKind.ConsumerPact
            ? new SchemaAnalysis("registry-json", "1", SchemaAssessment.NotAssessed, [], ["Pact changes are content changes; official Pact execution determines consumer impact."])
            : await comparer.CompareAsync(before.Artifact, after.Artifact, cancellationToken);
        var candidate = ContractComparison.Create(integration, Source(before), Source(after), command.BaselineRef, publisherId,
            CanonicalHash(before.Artifact) != CanonicalHash(after.Artifact), analysis, timeProvider.GetUtcNow());
        return await comparisons.AppendAsync(candidate, cancellationToken);
    }

    public Task<ContractComparison?> GetAsync(Guid id, CancellationToken cancellationToken) => comparisons.GetAsync(id, cancellationToken);
    private static ComparisonSource Source(PublishedContract value) => new(value.Publication.Id, value.Binding.Id, value.Binding.SourceVersionId,
        value.Binding.Source, value.Binding.Kind, value.Artifact.Sha256, value.Publication.Provenance.SourceRef);

    // Structural JSON equality only, including arrays and number spellings. This is not Pact matching.
    private static string CanonicalHash(ContractArtifact artifact)
    {
        var content = artifact.GetContent();
        using var document = JsonDocument.Parse(content.AsMemory(content.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(document.RootElement, writer);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }
    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}
