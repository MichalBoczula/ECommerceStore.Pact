using System.Diagnostics;
using ECommerceStore.Pact.Api.Health;
using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Application;
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
            context.ProblemDetails.Detail = context.ProblemDetails.Status == 503
                ? "Registry storage is unavailable."
                : "An unexpected error occurred.";
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
            Description = "Application versions and declared integrations. Contract artifacts and verification evidence are subsequent stages."
        };
        return Task.CompletedTask;
    });
});
builder.Services.AddRegistryInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ApplicationVersionService>();
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddExceptionHandler<RegistryExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapRegistryEndpoints();
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
