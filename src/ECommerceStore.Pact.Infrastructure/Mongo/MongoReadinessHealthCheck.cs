using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Mongo;

public sealed class MongoReadinessHealthCheck(IMongoDatabase database, IOptions<MongoOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("MongoDB readiness check timed out.");
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException)
        {
            // Do not attach driver exceptions: they can contain hosts or credentials.
            return HealthCheckResult.Unhealthy("MongoDB is unavailable.");
        }
    }
}
