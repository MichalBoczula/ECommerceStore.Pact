using System.Text;
using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using Moq;

namespace ECommerceStore.Pact.Application.Tests;

public sealed class ContractPublicationServiceTests
{
    private const string Repository = "https://github.com/owner/repo";
    private static readonly string Sha = new('a', 40);
    private readonly Mock<IApplicationVersionRepository> _versions = new(MockBehavior.Strict);
    private readonly Mock<IIntegrationRepository> _integrations = new(MockBehavior.Strict);
    private readonly Mock<IContractRepository> _contracts = new(MockBehavior.Strict);
    private readonly Mock<IContractDocumentValidator> _validator = new(MockBehavior.Strict);
    private ContractPublicationService Service => new(_versions.Object, _integrations.Object, _contracts.Object, _validator.Object, TimeProvider.System);
    private static ContractPublicationCommand Command => new("invoice", Sha, "ProviderOpenApi", null, null, "refs/heads/main",
        Repository + "/actions/runs/1", Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")));

    [Fact]
    public async Task MissingSourceVersionNeverPersistsOrValidatesArtifacts()
    {
        _versions.Setup(value => value.GetAsync(new ApplicationVersionIdentity("invoice", Sha), CancellationToken.None))
            .ReturnsAsync((ApplicationVersion?)null);
        var error = await Assert.ThrowsAsync<RegistryReferenceNotFoundException>(() => Service.PublishAsync(Command, "invoice-ci", CancellationToken.None));
        Assert.Equal("application_version_not_found", error.Code);
        _contracts.VerifyNoOtherCalls();
        _validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConsumerPactCannotClaimAnotherSourceApplication()
    {
        await Assert.ThrowsAsync<RegistryValidationException>(() => Service.PublishAsync(Command with
        {
            Kind = "ConsumerPact",
            ConsumerApplicationId = "payments",
            ProviderApplicationId = "invoice"
        }, "invoice-ci", CancellationToken.None));
        _versions.VerifyNoOtherCalls();
        _contracts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConflictingBindingCannotProduceAPublication()
    {
        var version = SetUpVersionAndValidator();
        var other = ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes("{ }"), DateTimeOffset.UtcNow);
        var binding = ContractBinding.Create(version, other, null, DateTimeOffset.UtcNow);
        _contracts.Setup(value => value.BindAsync(It.IsAny<ContractArtifact>(), It.IsAny<ContractBinding>(), CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<ContractBinding>(binding, false));
        await Assert.ThrowsAsync<ContractConflictException>(() => Service.PublishAsync(Command, "invoice-ci", CancellationToken.None));
        _contracts.Verify(value => value.BindAsync(It.IsAny<ContractArtifact>(), It.IsAny<ContractBinding>(), CancellationToken.None), Times.Once);
        _contracts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetryReturnsCanonicalArtifactAndPublicationMetadata()
    {
        var version = SetUpVersionAndValidator();
        var artifact = ContractArtifact.Create(ContractKind.ProviderOpenApi, "3.0.3", Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow.AddDays(-1));
        var binding = ContractBinding.Create(version, artifact, null, artifact.StoredAt);
        var publication = ContractPublication.Create(binding, new ContractProvenance(Command.SourceRef, Command.BuildUrl, "invoice-ci", Repository), artifact.StoredAt);
        _contracts.Setup(value => value.BindAsync(It.IsAny<ContractArtifact>(), It.IsAny<ContractBinding>(), CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<ContractBinding>(binding, false));
        _contracts.Setup(value => value.GetArtifactAsync(artifact.Sha256, CancellationToken.None)).ReturnsAsync(artifact);
        _contracts.Setup(value => value.PublishAsync(It.IsAny<ContractPublication>(), Repository, CancellationToken.None))
            .ReturnsAsync(new RegistrationResult<ContractPublication>(publication, false));
        var result = await Service.PublishAsync(Command, "invoice-ci", CancellationToken.None);
        Assert.False(result.Created);
        Assert.Same(artifact, result.Value.Artifact);
        Assert.Same(publication, result.Value.Publication);
    }

    private ApplicationVersion SetUpVersionAndValidator()
    {
        var version = ApplicationVersion.Register(new ApplicationVersionIdentity("invoice", Sha), Repository, null, DateTimeOffset.UtcNow);
        _versions.Setup(value => value.GetAsync(version.Identity, CancellationToken.None)).ReturnsAsync(version);
        _validator.Setup(value => value.ValidateAsync(ContractKind.ProviderOpenApi, It.IsAny<byte[]>(), null, CancellationToken.None)).ReturnsAsync("3.0.3");
        return version;
    }
}
