using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Scheduling.Api.OpenApi;

/// <summary>Declara o esquema <c>bearerAuth</c> (JWT) e metadados do documento OpenAPI v1.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "bearerAuth";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "ScheduleManager API",
            Version = "1.0.0",
            Description = "API de agendamento online (feature 001-online-scheduling-api). Erros em RFC 7807 com extensões `code` e `traceId`.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token obtido em POST /api/v1/auth/login.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
