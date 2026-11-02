using ECommerceStore.Pact.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRegistryInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MongoOptions>, MongoOptionsValidator>();
        services.AddOptions<MongoOptions>()
            .Bind(configuration.GetSection(MongoOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IMongoClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            settings.ConnectTimeout = timeout;
            settings.ServerSelectionTimeout = timeout;
            settings.SocketTimeout = timeout;
            return new MongoClient(settings);
        });
        services.AddSingleton(provider => provider.GetRequiredService<IMongoClient>()
            .GetDatabase(provider.GetRequiredService<IOptions<MongoOptions>>().Value.DatabaseName));
        services.AddHealthChecks().AddCheck<MongoReadinessHealthCheck>("mongodb", tags: ["ready"]);
        return services;
    }
}
