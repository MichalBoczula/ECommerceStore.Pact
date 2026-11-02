using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class ContractComparisonTests
{
    [Fact]
    public void StoredProfileIsPreservedAndSeparatesComparisonIdentity()
    {
        var before = Source('a', "refs/heads/main"); var after = Source('b', "refs/heads/feature");
        var integration = new IntegrationIdentity("payments", "invoice");
        var schema = new SchemaAnalysis("oasdiff", "1.35.0", SchemaAssessment.NoBreakingChangesDetected, [], []);
        var current = ContractComparison.Create(integration, before, after, before.SourceRef, "invoice-ci", true, schema, DateTimeOffset.UtcNow);
        var historical = ContractComparison.Rehydrate(current.Id, integration, before, after, before.SourceRef, "invoice-ci", true, schema,
            current.ComparedAt, "historical-profile-v0");
        Assert.Equal("historical-profile-v0", historical.Profile);
        Assert.NotEqual(current.Key, historical.Key);
        Assert.Equal(current.Id, historical.Id);
        Assert.Equal(current.ComparedAt, historical.ComparedAt);
    }

    [Fact]
    public void FindingsAreImmutableAndPartialAnalysisCannotBeLabeledSafe()
    {
        var findings = new List<SchemaFinding> { new("response-optional-property-added", 1, "Currency added", "GET", "/orders") };
        var analysis = new SchemaAnalysis("oasdiff", "1.35.0", SchemaAssessment.NoBreakingChangesDetected, findings, []);
        findings.Clear();
        Assert.Single(analysis.Findings);
        Assert.Throws<RegistryValidationException>(() => new SchemaAnalysis("oasdiff", "1.35.0", SchemaAssessment.NoBreakingChangesDetected, [], ["Unsupported keyword"]));
        Assert.Throws<RegistryValidationException>(() => new SchemaAnalysis("oasdiff", "1.35.0", SchemaAssessment.NoBreakingChangesDetected,
            [new("response-required-property-removed", 3, "Amount removed", "GET", "/orders")], []));
    }

    private static ComparisonSource Source(char revision, string sourceRef) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new ApplicationVersionIdentity("invoice", new string(revision, 40)), ContractKind.ProviderOpenApi, new string(revision, 64), sourceRef);
}
