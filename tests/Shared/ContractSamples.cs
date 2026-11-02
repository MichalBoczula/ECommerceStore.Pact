namespace ECommerceStore.Pact.Testing;

internal static class ContractSamples
{
    internal const string OpenApi = """
        {"openapi":"3.0.3","info":{"title":"Orders – zażółć","version":"1"},"paths":{"/orders/{id}":{"get":{"parameters":[{"in":"path","name":"id","required":true,"schema":{"type":"string"}}],"responses":{"200":{"description":"Order"}}}}}}
        """;
    internal const string Pact3 = """
        {"consumer":{"name":"payments"},"provider":{"name":"invoice"},"interactions":[{"description":"read an order","request":{"method":"GET","path":"/orders/123"},"response":{"status":200,"body":{"totalAmount":12.34}}}],"metadata":{"pactSpecification":{"version":"3.0.0"}}}
        """;
    internal const string Pact4 = """
        {"consumer":{"name":"payments"},"provider":{"name":"invoice"},"interactions":[{"type":"Synchronous/HTTP","description":"read an order","request":{"method":"GET","path":"/orders/123"},"response":{"status":200,"body":{"content":{"totalAmount":12.34},"contentType":"application/json","encoded":false}}}],"metadata":{"pactSpecification":{"version":"4.0"}}}
        """;
}
