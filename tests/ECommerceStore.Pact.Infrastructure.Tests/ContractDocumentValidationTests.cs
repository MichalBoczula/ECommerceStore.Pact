using System.Text;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Infrastructure.Contracts;
using ECommerceStore.Pact.Testing;

namespace ECommerceStore.Pact.Infrastructure.Tests;

public sealed class ContractDocumentValidationTests
{
    private readonly ContractDocumentValidator _validator = new();

    [Theory]
    [InlineData("3.0.3")]
    [InlineData("3.1.0")]
    public async Task ReadsSupportedOpenApiWithThePinnedOfficialReader(string specification)
    {
        var bytes = Encoding.UTF8.GetBytes(ContractSamples.OpenApi.Replace("3.0.3", specification, StringComparison.Ordinal));
        Assert.Equal(specification, await _validator.ValidateAsync(ContractKind.ProviderOpenApi, bytes, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptsHttpPact3And4WithoutRewritingMatchingRules(bool v4)
    {
        var bytes = Encoding.UTF8.GetBytes(v4 ? ContractSamples.Pact4 : ContractSamples.Pact3);
        Assert.Equal(v4 ? "4.0.0" : "3.0.0", await _validator.ValidateAsync(ContractKind.ConsumerPact, bytes,
            new IntegrationIdentity("payments", "invoice"), CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"openapi\":\"3.0.3\",\"openapi\":\"3.1.0\"}")]
    [InlineData("{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"A\",\"version\":\"1\"},\"paths\":{}}")]
    [InlineData("{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"A\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{\"Bad\":{\"$ref\":\"https://example.test/schema.json\"}}}}")]
    public async Task RejectsInvalidUnsupportedOrExternallyReferencedDocuments(string json) =>
        await Assert.ThrowsAsync<RegistryValidationException>(() => _validator.ValidateAsync(ContractKind.ProviderOpenApi,
            Encoding.UTF8.GetBytes(json), null, CancellationToken.None));

    [Fact]
    public async Task RejectsInvalidUtf8BeforeParsing() =>
        await Assert.ThrowsAsync<RegistryValidationException>(() => _validator.ValidateAsync(ContractKind.ProviderOpenApi,
            [0xff, 0xfe, 0x00], null, CancellationToken.None));

    [Fact]
    public async Task PactBodyReferencePropertiesRemainOpaqueBusinessData()
    {
        var json = ContractSamples.Pact3.Replace("\"totalAmount\":12.34", "\"$ref\":\"https://example.test/business-data\"", StringComparison.Ordinal);
        Assert.Equal("3.0.0", await _validator.ValidateAsync(ContractKind.ConsumerPact, Encoding.UTF8.GetBytes(json),
            new IntegrationIdentity("payments", "invoice"), CancellationToken.None));
    }

    [Theory]
    [InlineData("party")]
    [InlineData("specification")]
    [InlineData("interaction")]
    [InlineData("status")]
    [InlineData("plugin")]
    public async Task RejectsUnsupportedOrMismatchedPactProfiles(string change)
    {
        var json = change switch
        {
            "party" => ContractSamples.Pact4.Replace("payments", "someone-else", StringComparison.Ordinal),
            "specification" => ContractSamples.Pact4.Replace("\"4.0\"", "\"5.0\"", StringComparison.Ordinal),
            "interaction" => ContractSamples.Pact4.Replace("Synchronous/HTTP", "Asynchronous/Messages", StringComparison.Ordinal),
            "status" => ContractSamples.Pact4.Replace("\"status\":200", "\"status\":\"200\"", StringComparison.Ordinal),
            _ => ContractSamples.Pact4.Replace("\"metadata\":{", "\"metadata\":{\"plugins\":[{\"name\":\"unknown\"}],", StringComparison.Ordinal)
        };
        await Assert.ThrowsAsync<RegistryValidationException>(() => _validator.ValidateAsync(ContractKind.ConsumerPact,
            Encoding.UTF8.GetBytes(json), new IntegrationIdentity("payments", "invoice"), CancellationToken.None));
    }
}
