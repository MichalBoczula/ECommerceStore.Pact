using System.Diagnostics;
using ECommerceStore.Pact.Api.Reporting;
using ECommerceStore.Pact.Application.Reporting;
using ECommerceStore.Pact.Api.Compatibility;
using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Api.History;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Api.Verification;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Api.Health;
using ECommerceStore.Pact.Api.Contracts;
using ECommerceStore.Pact.Api.Security;
using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

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
                ? "A registry dependency is unavailable."
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
            Description = "Immutable contract artifacts, source versions, publication provenance and provider CI reports. Upload acceptance is not verification evidence."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[PublisherAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "Opaque publisher token scoped to application IDs." };
        foreach (var path in document.Paths.Values)
            foreach (var operation in path.Operations ?? [])
                if (operation.Key.ToString().Equals("POST", StringComparison.OrdinalIgnoreCase))
                    operation.Value.Security = [new OpenApiSecurityRequirement
                    { [new OpenApiSecuritySchemeReference(PublisherAuthenticationHandler.SchemeName, document)] = [] }];
        return Task.CompletedTask;
    });
});
builder.Services.AddRegistryInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ApplicationVersionService>();
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddScoped<ContractPublicationService>();
builder.Services.AddScoped<VerificationRunService>();
builder.Services.AddScoped<ContractComparisonService>();
builder.Services.AddScoped<IssueHistoryService>();
builder.Services.AddScoped<VerificationProfileService>();
builder.Services.AddScoped<CompatibilityDecisionService>();
builder.Services.AddScoped<RegistryReadService>();
builder.Services.AddSingleton<IValidateOptions<RegistryAuthOptions>, RegistryAuthValidator>();
builder.Services.AddOptions<RegistryAuthOptions>().Bind(builder.Configuration.GetSection("RegistryAuth")).ValidateOnStart();
builder.Services.AddAuthentication(PublisherAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PublisherAuthenticationHandler>(PublisherAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder().AddPolicy("RegistryWrite", policy => policy.RequireAuthenticatedUser());
builder.Services.AddExceptionHandler<RegistryExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("../openapi/v1.json", "ECommerceStore Pact Registry v1"));
app.MapRegistryEndpoints();
app.MapContractEndpoints();
app.MapVerificationEndpoints();
app.MapHistoryEndpoints();
app.MapCompatibilityEndpoints();
app.MapRegistryReadEndpoints();
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
