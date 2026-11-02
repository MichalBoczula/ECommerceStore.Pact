using System.Text;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class ContractIdentityTests
{
    [Fact]
    public void HashUsesOriginalBytesAndContentCannotBeMutatedThroughItsArrays()
    {
        var source = Encoding.UTF8.GetBytes("abc");
        var artifact = ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", source, DateTimeOffset.UtcNow);
        source[0] = (byte)'x';
        var copy = artifact.GetContent();
        copy[1] = (byte)'y';
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", artifact.Sha256);
        Assert.Equal("abc", Encoding.UTF8.GetString(artifact.GetContent()));
    }

    [Fact]
    public void WhitespaceChangesTheContentIdentity()
    {
        var first = ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow);
        var second = ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes("{}\r\n"), DateTimeOffset.UtcNow);
        Assert.NotEqual(first.Sha256, second.Sha256);
    }

    [Fact]
    public void RehydrationDetectsChangedStoredBytes() => Assert.Throws<InvalidOperationException>(() =>
        ContractArtifact.Rehydrate(new string('a', 64), ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("main")]
    [InlineData("refs/heads/")]
    [InlineData("refs/heads/a..b")]
    [InlineData("refs/heads/my branch")]
    public void RequiresFullSourceReferences(string sourceRef) => Assert.Throws<RegistryValidationException>(() =>
        new ContractProvenance(sourceRef, "https://github.com/owner/repo/actions/runs/1", "ci", "https://github.com/owner/repo"));

    [Theory]
    [InlineData("https://github.com/other/repo/actions/runs/1")]
    [InlineData("https://github.com/owner/repository/actions/runs/1")]
    [InlineData("http://github.com/owner/repo/actions/runs/1")]
    [InlineData("https://github.com/owner/repo/actions/runs/1?token=secret")]
    public void BuildProvenanceMustBelongToTheSourceRepository(string buildUrl) => Assert.Throws<RegistryValidationException>(() =>
        new ContractProvenance("refs/heads/main", buildUrl, "ci", "https://github.com/owner/repo"));

    [Fact]
    public void BranchAndBuildProvenanceHaveSeparateIdentities()
    {
        var first = new ContractProvenance("refs/heads/main", "https://github.com/owner/repo/actions/runs/1", "ci", "https://github.com/owner/repo");
        var retry = new ContractProvenance(first.SourceRef, first.BuildUrl, "ci", "https://github.com/owner/repo");
        var other = new ContractProvenance("refs/heads/feature", first.BuildUrl, "ci", "https://github.com/owner/repo");
        Assert.Equal(first.IdentityHash, retry.IdentityHash);
        Assert.NotEqual(first.IdentityHash, other.IdentityHash);
    }

    [Fact]
    public void BuildProvenanceAcceptsTheBrowserUrlOfARegisteredGitCloneUrl()
    {
        var value = new ContractProvenance("refs/heads/main", "https://github.com/owner/repo/actions/runs/1", "ci", "https://github.com/owner/repo.git");
        Assert.Equal("https://github.com/owner/repo/actions/runs/1", value.BuildUrl);
    }
}
