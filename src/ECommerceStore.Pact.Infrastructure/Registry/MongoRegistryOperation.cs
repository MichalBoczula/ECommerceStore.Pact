using ECommerceStore.Pact.Application;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal static class MongoRegistryOperation
{
    internal static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException)
        {
            throw new RegistryUnavailableException(exception);
        }
    }

    internal static bool IsDuplicateKey(Exception exception) => exception is
        MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } or
        MongoCommandException { Code: 11000 };
}
