using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class ApplicationVersionTests
{
    private static readonly ApplicationVersionIdentity Identity = new("payments", new string('a', 40));
    private const string RepositoryUrl = "https://github.com/MichalBoczula/ECommerceStorePayments";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("https://github.com")]
    [InlineData("https://user:secret@github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo?token=secret")]
    [InlineData("https://github.com/owner/repo#main")]
    public void RejectsInvalidOrCredentialBearingRepositoryUrls(string? url) =>
        Assert.Throws<RegistryValidationException>(() => ApplicationVersion.Register(Identity, url, null, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("sha256:abc123")]
    public void RejectsImageTagsAndInvalidDigests(string digest) =>
        Assert.Throws<RegistryValidationException>(() => ApplicationVersion.Register(Identity, RepositoryUrl, digest, DateTimeOffset.UtcNow));

    [Fact]
    public void NormalizesMetadataAndPreservesIdentityAcrossPersistencePrecision()
    {
        var submitted = DateTimeOffset.Parse("2026-11-02T18:00:00.1234567+01:00");
        var version = ApplicationVersion.Register(Identity, RepositoryUrl + "/", "SHA256:" + new string('B', 64), submitted);
        var readBack = ApplicationVersion.Rehydrate(version.Id, Identity, version.RepositoryUrl, version.ImageDigest, version.RegisteredAt);
        Assert.Equal(RepositoryUrl, readBack.RepositoryUrl);
        Assert.Equal("sha256:" + new string('b', 64), readBack.ImageDigest);
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T17:00:00.123Z"), readBack.RegisteredAt);
        Assert.True(version.HasSameMetadata(readBack));
        Assert.Equal(version.Id, readBack.Id);
    }

    [Fact]
    public void OmittedImageDigestIsDistinctImmutableMetadata()
    {
        var original = ApplicationVersion.Register(Identity, RepositoryUrl, null, DateTimeOffset.UtcNow);
        var withImage = ApplicationVersion.Register(Identity, RepositoryUrl, "sha256:" + new string('b', 64), DateTimeOffset.UtcNow);
        Assert.False(original.HasSameMetadata(withImage));
    }
}
