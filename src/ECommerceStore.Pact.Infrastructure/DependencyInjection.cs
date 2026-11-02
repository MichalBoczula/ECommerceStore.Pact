using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Infrastructure.Compatibility;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Infrastructure.History;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Infrastructure.Verification;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Infrastructure.Contracts;
using ECommerceStore.Pact.Infrastructure.Mongo;
using ECommerceStore.Pact.Infrastructure.Registry;
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
        services.AddSingleton<RegistryCollections>();
        services.AddSingleton<RegistryIndexes>();
        services.AddSingleton<IApplicationVersionRepository, MongoApplicationVersionRepository>();
        services.AddSingleton<IIntegrationRepository, MongoIntegrationRepository>();
        services.AddSingleton<IContractRepository, MongoContractRepository>();
        services.AddSingleton<IVerificationProfileRepository, MongoCompatibilityRepository>();
        services.AddSingleton<ICompatibilityDecisionRepository, MongoCompatibilityRepository>();
        services.AddSingleton<ICompatibilityEvidenceReader, MongoCompatibilityEvidenceReader>();
        services.AddSingleton<IContractComparisonRepository, MongoContractComparisonRepository>();
        services.AddSingleton<IContractSchemaComparer, OasDiffSchemaComparer>();
        services.AddSingleton<IVerificationRunRepository, MongoVerificationRunRepository>();
        services.AddSingleton<IContractDocumentValidator, ContractDocumentValidator>();
        services.AddHealthChecks().AddCheck<MongoReadinessHealthCheck>("mongodb", tags: ["ready"]);
        return services;
    }
}
