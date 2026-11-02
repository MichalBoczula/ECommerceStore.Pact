using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class RegistryIdentityTests
{
    private static readonly string Sha = new('a', 40);

    [Theory]
    [InlineData(40)]
    [InlineData(64)]
    public void VersionIdentityNormalizesCaseAndRequiresTheFullCommit(int length)
    {
        var first = new ApplicationVersionIdentity(" Payments-Api ", new string('A', length));
        var second = new ApplicationVersionIdentity("payments-api", new string('a', length));
        Assert.Equal(first, second);
        Assert.Equal("payments-api", first.ApplicationId);
        Assert.Equal(new string('a', length), first.CommitSha);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../payments")]
    [InlineData("payments--api")]
    [InlineData("-payments")]
    [InlineData("payments-")]
    [InlineData("płatności")]
    public void RejectsInvalidApplicationIdentities(string? applicationId)
    {
        var error = Assert.Throws<RegistryValidationException>(() => new ApplicationVersionIdentity(applicationId, Sha));
        Assert.Equal("applicationId", error.Field);
    }

    [Fact]
    public void RejectsOverlongApplicationId() =>
        Assert.Throws<RegistryValidationException>(() => new ApplicationVersionIdentity(new string('a', 101), Sha));

    [Theory]
    [InlineData(null)]
    [InlineData("main")]
    [InlineData("abc1234")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void RejectsBranchNamesAbbreviationsAndNonHexCommits(string? commit)
    {
        var error = Assert.Throws<RegistryValidationException>(() => new ApplicationVersionIdentity("payments", commit));
        Assert.Equal("commitSha", error.Field);
    }

    [Fact]
    public void IntegrationIdentityIsDirected()
    {
        var forward = new IntegrationIdentity(" Payments ", "INVOICE");
        Assert.Equal(new IntegrationIdentity("payments", "invoice"), forward);
        Assert.NotEqual(new IntegrationIdentity("invoice", "payments"), forward);
    }

    [Fact]
    public void RejectsSelfIntegrationAfterNormalization() =>
        Assert.Throws<RegistryValidationException>(() => new IntegrationIdentity(" Payments ", "payments"));
}
