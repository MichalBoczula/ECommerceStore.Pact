using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Domain;
using Moq;

namespace ECommerceStore.Pact.Application.Tests;

public sealed class RegistrationServiceTests
{
    private static readonly string Sha = new('a', 40);
    private const string RepositoryUrl = "https://github.com/MichalBoczula/ECommerceStorePayments";

    [Fact]
    public async Task RegisterUsesTheClockAndPassesTheCancellationToken()
    {
        var clock = new FixedTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IApplicationVersionRepository>(MockBehavior.Strict);
        repository.Setup(value => value.RegisterAsync(It.Is<ApplicationVersion>(candidate =>
                candidate.Identity.ApplicationId == "payments" && candidate.RegisteredAt == clock.GetUtcNow()), cancellation.Token))
            .ReturnsAsync((ApplicationVersion candidate, CancellationToken _) => new RegistrationResult<ApplicationVersion>(candidate, true));
        var service = new ApplicationVersionService(repository.Object, clock);

        var result = await service.RegisterAsync("Payments", Sha, RepositoryUrl, null, cancellation.Token);

        Assert.True(result.Created);
        repository.VerifyAll();
    }

    [Fact]
    public async Task RetryReturnsTheStoredIdentityAndTimestamp()
    {
        var stored = ApplicationVersion.Register(new ApplicationVersionIdentity("payments", Sha), RepositoryUrl, null,
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var repository = new Mock<IApplicationVersionRepository>(MockBehavior.Strict);
        repository.Setup(value => value.RegisterAsync(It.IsAny<ApplicationVersion>(), CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<ApplicationVersion>(stored, false));

        var result = await new ApplicationVersionService(repository.Object, new FixedTimeProvider())
            .RegisterAsync("payments", Sha, RepositoryUrl, null, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Same(stored, result.Value);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("digest")]
    public async Task RejectsConflictingImmutableMetadata(string difference)
    {
        var stored = ApplicationVersion.Register(new ApplicationVersionIdentity("payments", Sha),
            difference == "repository" ? "https://github.com/another/repository" : RepositoryUrl,
            difference == "digest" ? "sha256:" + new string('b', 64) : null, DateTimeOffset.UtcNow);
        var repository = new Mock<IApplicationVersionRepository>(MockBehavior.Strict);
        repository.Setup(value => value.RegisterAsync(It.IsAny<ApplicationVersion>(), CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<ApplicationVersion>(stored, false));
        var service = new ApplicationVersionService(repository.Object, new FixedTimeProvider());

        await Assert.ThrowsAsync<RegistryConflictException>(() =>
            service.RegisterAsync("payments", Sha, RepositoryUrl, null, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidVersionNeverReachesPersistence()
    {
        var repository = new Mock<IApplicationVersionRepository>(MockBehavior.Strict);
        var service = new ApplicationVersionService(repository.Object, new FixedTimeProvider());
        await Assert.ThrowsAsync<RegistryValidationException>(() =>
            service.RegisterAsync("payments", "main", RepositoryUrl, null, CancellationToken.None));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LookupUsesNormalizedImmutableIdentity()
    {
        var identity = new ApplicationVersionIdentity("payments", Sha);
        var repository = new Mock<IApplicationVersionRepository>(MockBehavior.Strict);
        repository.Setup(value => value.GetAsync(identity, CancellationToken.None)).ReturnsAsync((ApplicationVersion?)null);
        var result = await new ApplicationVersionService(repository.Object, new FixedTimeProvider())
            .GetAsync("PAYMENTS", Sha.ToUpperInvariant(), CancellationToken.None);
        Assert.Null(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task IntegrationRetryReturnsStoredRecord()
    {
        var stored = Integration.Register(new IntegrationIdentity("payments", "invoice"), DateTimeOffset.UtcNow);
        var repository = new Mock<IIntegrationRepository>(MockBehavior.Strict);
        repository.Setup(value => value.RegisterAsync(It.Is<Integration>(candidate => candidate.Identity == stored.Identity), CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<Integration>(stored, false));
        var result = await new IntegrationService(repository.Object, new FixedTimeProvider())
            .RegisterAsync("PAYMENTS", "invoice", CancellationToken.None);
        Assert.False(result.Created);
        Assert.Same(stored, result.Value);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-02T17:00:00Z");
    }
}
