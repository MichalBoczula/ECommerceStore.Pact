using ECommerceStore.Pact.Infrastructure.Mongo;

namespace ECommerceStore.Pact.Infrastructure.Tests;

public sealed class MongoOptionsTests
{
    private readonly MongoOptionsValidator _validator = new();

    [Theory]
    [InlineData("mongodb://localhost:27017")]
    [InlineData("mongodb://localhost:27017/?authSource=admin")]
    public void AcceptsValidConfiguration(string connectionString)
    {
        var result = _validator.Validate(null, new MongoOptions { ConnectionString = connectionString });
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://user:private-value@example.test")]
    public void RejectsMissingOrInvalidConnectionStringWithoutEchoingIt(string connectionString)
    {
        var result = _validator.Validate(null, new MongoOptions { ConnectionString = connectionString });
        Assert.True(result.Failed);
        Assert.DoesNotContain("private-value", string.Join(" ", result.Failures!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid/name")]
    [InlineData("invalid.name")]
    [InlineData("invalid$name")]
    public void RejectsInvalidDatabaseName(string databaseName)
    {
        var result = _validator.Validate(null, new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName
        });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void RejectsUnboundedTimeout(int timeoutSeconds)
    {
        var result = _validator.Validate(null, new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            TimeoutSeconds = timeoutSeconds
        });
        Assert.True(result.Failed);
    }
}
