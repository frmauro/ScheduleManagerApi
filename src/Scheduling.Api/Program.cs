using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Api.ErrorHandling;
using Scheduling.Api.OpenApi;
using Scheduling.Application;
using Scheduling.Infrastructure;
using Scheduling.Infrastructure.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication();

builder.Services.AddScoped<FluentValidationFilter>();
builder.Services
    .AddControllers(options =>
    {
        options.Filters.AddService<FluentValidationFilter>();

        // Campos obrigatórios são validados pelos validators FluentValidation (mensagens e códigos padronizados).
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = FluentValidationFilter.InvalidModelStateResponse);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var http = context.HttpContext;
    if (http.Response.HasStarted || http.Response.ContentLength is > 0)
    {
        return;
    }

    var problem = Problems.Create(http, http.Response.StatusCode, http.Response.StatusCode == StatusCodes.Status404NotFound ? "route.not_found" : "http.error",
        ReasonPhrases(http.Response.StatusCode), null);
    await Problems.WriteAsync(http, problem);
});

// Log de requisições sem corpo nem headers (nunca registra senhas, tokens ou Authorization).
app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    await next();
    app.Logger.LogInformation("{Method} {Path} → {StatusCode} em {ElapsedMs:0.0} ms",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
});

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ScheduleManager API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();

app.Run();

static string ReasonPhrases(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : "Erro";

/// <summary>Exposto para <c>WebApplicationFactory</c> nos testes de integração.</summary>
public partial class Program;
