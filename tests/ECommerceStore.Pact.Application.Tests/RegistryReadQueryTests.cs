using ECommerceStore.Pact.Application.Reporting;
using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Application.Tests;

public sealed class RegistryReadQueryTests
{
    [Fact]
    public void CursorIsScopedToResourceAndNormalizedFiltersButNotPageSize()
    {
        var filter = new RegistryReadFilter { ConsumerApplicationId = " PAYMENTS ", ProviderApplicationId = "INVOICE", Status = "failed" };
        var query = RegistryReadQuery.Create(RegistryResource.Runs, filter, 1);
        var position = new RegistryPosition(Guid.NewGuid().ToString("D"), 1760000000000);
        var page = query.Page(new RegistrySlice<int>([42], position));
        var normalized = filter with { ConsumerApplicationId = "payments", ProviderApplicationId = "invoice", Status = "Failed" };

        Assert.Equal(position, RegistryReadQuery.Create(RegistryResource.Runs, normalized, 100, page.NextCursor).After);
        Assert.Throws<RegistryValidationException>(() => RegistryReadQuery.Create(RegistryResource.Decisions, normalized, 1, page.NextCursor));
        Assert.Throws<RegistryValidationException>(() => RegistryReadQuery.Create(RegistryResource.Runs, normalized with { Status = "Passed" }, 1, page.NextCursor));
        Assert.Null(query.Page(new RegistrySlice<int>([], null)).NextCursor);
    }

    [Fact]
    public void ApplicationCursorUsesAnApplicationKeyInsteadOfARecordId()
    {
        var query = RegistryReadQuery.Create(RegistryResource.Applications, new() { Search = "PAY" });
        var cursor = query.Page(new RegistrySlice<int>([1], new("payments", null))).NextCursor;
        Assert.Equal(new("payments", null), RegistryReadQuery.Create(RegistryResource.Applications, new() { Search = "pay" }, after: cursor).After);
        Assert.Equal("required-pact", RegistryReadQuery.Create(RegistryResource.Profiles, new() { Name = " REQUIRED-PACT " }).Filter.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidPageSizesAreRejected(int limit) =>
        Assert.Throws<RegistryValidationException>(() => RegistryReadQuery.Create(RegistryResource.Runs, new(), limit));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("{}")]
    [InlineData("eyJWZXJzaW9uIjoxfQ")]
    public void InvalidCursorPayloadsAreValidationFailures(string cursor) =>
        Assert.Equal("after", Assert.Throws<RegistryValidationException>(() => RegistryReadQuery.Create(RegistryResource.Runs, new(), after: cursor)).Field);

    [Fact]
    public void NumericEnumValuesPartialCommitsAndEmptySelectorsAreRejected()
    {
        RegistryReadFilter[] invalid = [new() { Status = "0" }, new() { Kind = "1" }, new() { ConsumerCommitSha = "abc123" },
            new() { ProviderCommitSha = "" }, new() { ProfileId = Guid.Empty }, new() { CiAttempt = 0 }, new() { Search = ".*" }, new() { JsonPath = "" }];
        foreach (var filter in invalid) Assert.Throws<RegistryValidationException>(() => RegistryReadQuery.Create(RegistryResource.Runs, filter));
    }
}
