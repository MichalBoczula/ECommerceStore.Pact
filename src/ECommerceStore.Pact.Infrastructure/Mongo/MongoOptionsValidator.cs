using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Mongo;

public sealed class MongoOptionsValidator : IValidateOptions<MongoOptions>
{
    public ValidateOptionsResult Validate(string? name, MongoOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Mongo:ConnectionString is required.");
        }
        else
        {
            try
            {
                _ = new MongoUrl(options.ConnectionString);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or MongoConfigurationException)
            {
                failures.Add("Mongo:ConnectionString must be a valid MongoDB connection string.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseName) || options.DatabaseName.Length > 63 ||
            options.DatabaseName.IndexOfAny(['/', '\\', '.', ' ', '"', '$', '*', '<', '>', ':', '|', '?', '\0']) >= 0)
        {
            failures.Add("Mongo:DatabaseName must contain 1 to 63 characters and no reserved database-name characters.");
        }

        if (options.TimeoutSeconds is < 1 or > 30)
        {
            failures.Add("Mongo:TimeoutSeconds must be between 1 and 30.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
