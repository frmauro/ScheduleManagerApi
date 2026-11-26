using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Domain.Common;

namespace Scheduling.Api.ErrorHandling;

/// <summary>Construção padronizada de Problem Details (RFC 7807) com extensões <c>code</c> e <c>traceId</c>.</summary>
public static class Problems
{
    public const string ContentType = "application/problem+json";
    private const string TypeBase = "https://schedulemanager.dev/problems/";

    public static ProblemDetails Create(HttpContext context, int status, string code, string title, string? detail)
    {
        var problem = new ProblemDetails
        {
            Type = TypeBase + code,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path,
        };
        Enrich(problem, context, code);
        return problem;
    }

    public static ValidationProblemDetails Validation(HttpContext context, IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Type = TypeBase + ErrorCodes.ValidationFailed,
            Title = "Um ou mais campos são inválidos.",
            Status = StatusCodes.Status400BadRequest,
            Instance = context.Request.Path,
        };
        Enrich(problem, context, ErrorCodes.ValidationFailed);
        return problem;
    }

    public static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return context.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: ContentType);
    }

    /// <summary>Converte nomes de propriedade (ex.: "Address.PostalCode", "$.startAt") para o caminho camelCase do JSON.</summary>
    public static string ToJsonPath(string propertyName)
    {
        var trimmed = propertyName.StartsWith("$.", StringComparison.Ordinal) ? propertyName[2..] : propertyName.TrimStart('$');
        return string.Join('.', trimmed.Split('.').Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }

    private static void Enrich(ProblemDetails problem, HttpContext context, string code)
    {
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
    }
}
