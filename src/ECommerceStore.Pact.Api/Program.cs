using System.Diagnostics;
using ECommerceStore.Pact.Api.Health;
using ECommerceStore.Pact.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        if (context.ProblemDetails.Status >= 500)
        {
            context.ProblemDetails.Detail = "An unexpected error occurred.";
        }
    };
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "ECommerceStore Pact Registry",
            Version = "v1",
            Description = "Contract artifacts and compatibility history. Business endpoints arrive in subsequent backlog tasks."
        };
        return Task.CompletedTask;
    });
});
builder.Services.AddRegistryInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

public partial class Program { }
