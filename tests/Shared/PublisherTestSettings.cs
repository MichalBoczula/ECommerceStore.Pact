using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ECommerceStore.Pact.Testing;

internal static class PublisherTestSettings
{
    internal const string Token = "synthetic-registry-test-publisher-token-0001";
    internal const string PaymentsToken = "synthetic-payments-only-publisher-token-0002";
    internal static Dictionary<string, string?> Configuration => new()
    {
        ["RegistryAuth:Publishers:test-ci:TokenSha256"] = Hash(Token),
        ["RegistryAuth:Publishers:test-ci:ApplicationIds:0"] = "*",
        ["RegistryAuth:Publishers:payments-ci:TokenSha256"] = Hash(PaymentsToken),
        ["RegistryAuth:Publishers:payments-ci:ApplicationIds:0"] = "payments"
    };
    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    internal static void Authorize(HttpClient client, string token = Token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
