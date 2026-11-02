using System.Text;
using System.Text.Json.Nodes;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;
using ECommerceStore.Pact.Infrastructure.History;
using ECommerceStore.Pact.Testing;
using Microsoft.Extensions.Configuration;

namespace ECommerceStore.Pact.Infrastructure.Tests;

public sealed class SchemaComparisonTests
{
    [Theory]
    [InlineData("response-add", false)]
    [InlineData("response-remove", true)]
    [InlineData("response-optional", true)]
    [InlineData("request-required", true)]
    [InlineData("request-optional", false)]
    [InlineData("response-type", true)]
    [InlineData("response-nullable", true)]
    [InlineData("request-not-nullable", true)]
    [InlineData("response-nullable31", true)]
    [InlineData("request-not-nullable31", true)]
    [InlineData("anyof-refactor", false)]
    [InlineData("reference", true)]
    [InlineData("allof", true)]
    [InlineData("oneof", true)]
    public async Task PinnedToolRespectsRequestResponseDirectionTypesNullabilityAndComposition(string change, bool breaking)
    {
        var before = ComparisonSamples.OpenApi();
        var after = ComparisonSamples.OpenApi();
        var response = ComparisonSamples.Response(after);
        var request = ComparisonSamples.Request(after);
        switch (change)
        {
            case "response-add": response["properties"]!["currency"] = new JsonObject { ["type"] = "string" }; break;
            case "response-remove": response["properties"]!.AsObject().Remove("totalAmount"); response["required"] = new JsonArray(); break;
            case "response-optional": response["required"] = new JsonArray(); break;
            case "request-required": request["required"] = new JsonArray("customerId"); break;
            case "request-optional": ComparisonSamples.Request(before)["required"] = new JsonArray("customerId"); break;
            case "response-type": response["properties"]!["totalAmount"]!["type"] = "string"; break;
            case "response-nullable": response["properties"]!["totalAmount"]!["nullable"] = true; break;
            case "request-not-nullable": ComparisonSamples.Request(before)["properties"]!["customerId"]!["nullable"] = true; break;
            case "response-nullable31":
                before["openapi"] = "3.1.0"; after["openapi"] = "3.1.0";
                response["properties"]!["totalAmount"]!["type"] = new JsonArray("number", "null");
                break;
            case "request-not-nullable31":
                before["openapi"] = "3.1.0"; after["openapi"] = "3.1.0";
                ComparisonSamples.Request(before)["properties"]!["customerId"]!["type"] = new JsonArray("string", "null");
                break;
            case "anyof-refactor":
                before["openapi"] = "3.1.0"; after["openapi"] = "3.1.0";
                ComparisonSamples.Request(before)["properties"]!["customerId"] = JsonNode.Parse("{\"anyOf\":[{\"type\":\"string\",\"enum\":[\"a\",\"b\"]},{\"type\":\"null\"}]}");
                ComparisonSamples.Request(after)["properties"]!["customerId"] = JsonNode.Parse("{\"anyOf\":[{\"$ref\":\"#/components/schemas/CustomerId\"},{\"type\":\"null\"}]}");
                after["components"] = JsonNode.Parse("{\"schemas\":{\"CustomerId\":{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}}}");
                break;
            case "reference":
                foreach (var document in new[] { before, after })
                {
                    var schema = ComparisonSamples.Response(document).DeepClone();
                    document["components"] = new JsonObject { ["schemas"] = new JsonObject { ["Order"] = schema } };
                    ComparisonSamples.Response(document).Clear();
                    ComparisonSamples.Response(document)["$ref"] = "#/components/schemas/Order";
                }
                after["components"]!["schemas"]!["Order"]!["required"] = new JsonArray();
                break;
            case "allof":
                foreach (var document in new[] { before, after })
                {
                    var schema = ComparisonSamples.Response(document).DeepClone();
                    ComparisonSamples.Response(document).Clear();
                    ComparisonSamples.Response(document)["allOf"] = new JsonArray(schema);
                }
                ComparisonSamples.Response(after)["allOf"]![0]!["required"] = new JsonArray();
                break;
            case "oneof":
                foreach (var document in new[] { before, after })
                    ComparisonSamples.Request(document)["properties"]!["customerId"] = JsonNode.Parse("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"integer\"}]}");
                ComparisonSamples.Request(after)["properties"]!["customerId"]!["oneOf"]!.AsArray().RemoveAt(1);
                break;
        }
        using var comparer = new OasDiffSchemaComparer(new ConfigurationBuilder().Build());
        var result = await comparer.CompareAsync(Artifact(before), Artifact(after), CancellationToken.None);
        Assert.Equal(breaking ? SchemaAssessment.PotentiallyBreaking : SchemaAssessment.NoBreakingChangesDetected, result.Assessment);
        Assert.Equal("1.35.0", result.Version);
        if (change == "response-remove") Assert.Contains(result.Findings, f => f.Code == "response-required-property-removed" && f.Path == "/orders" && f.Method == "POST");
        if (!breaking) Assert.DoesNotContain(result.Findings, finding => finding.Level >= 2);
    }

    [Fact]
    public async Task AdvancedSemanticsAndDeprecationCannotBeReportedAsSafeByOmission()
    {
        var document = ComparisonSamples.OpenApi();
        ComparisonSamples.Response(document)["unevaluatedProperties"] = false;
        using var comparer = new OasDiffSchemaComparer(new ConfigurationBuilder().Build());
        var result = await comparer.CompareAsync(Artifact(document), Artifact(document), CancellationToken.None);
        Assert.Equal(SchemaAssessment.NotAssessed, result.Assessment);
        Assert.NotEmpty(result.Limitations);
    }

    [Fact]
    public async Task MissingToolReturnsUnavailableInsteadOfEmptyPassingAnalysis()
    {
        using var comparer = new OasDiffSchemaComparer(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Comparison:Executable"] = "/missing/pinned-oasdiff" }).Build());
        await Assert.ThrowsAsync<ComparisonUnavailableException>(() => comparer.CompareAsync(Artifact(ComparisonSamples.OpenApi()), Artifact(ComparisonSamples.OpenApi()), CancellationToken.None));
    }

    [Fact]
    public async Task ExternalReferencesAreDisabledEvenWhenInvokedOutsideUploadValidation()
    {
        var document = ComparisonSamples.OpenApi();
        ComparisonSamples.Response(document).Clear();
        ComparisonSamples.Response(document)["$ref"] = "http://127.0.0.1:1/never-fetch.json";
        using var comparer = new OasDiffSchemaComparer(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ComparisonUnavailableException>(() => comparer.CompareAsync(Artifact(document), Artifact(document), CancellationToken.None));
    }
    private static ContractArtifact Artifact(JsonObject document) => ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes(document.ToJsonString()), DateTimeOffset.UtcNow);
}
