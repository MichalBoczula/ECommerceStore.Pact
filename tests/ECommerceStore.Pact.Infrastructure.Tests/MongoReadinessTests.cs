using ECommerceStore.Pact.Infrastructure.Mongo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace ECommerceStore.Pact.Infrastructure.Tests;

public sealed class MongoReadinessTests
{
    [Fact]
    public async Task DatabaseFailureDoesNotAttachPrivateDriverException()
    {
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(), It.IsAny<ReadPreference>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoException("private-host-and-credentials"));
        var check = new MongoReadinessHealthCheck(database.Object, Options.Create(new MongoOptions()));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain("private-host", result.Description!);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(), It.IsAny<ReadPreference>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var check = new MongoReadinessHealthCheck(database.Object, Options.Create(new MongoOptions()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
    }
}
