using Microsoft.AspNetCore.Diagnostics;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Common;

namespace Scheduling.Api.ErrorHandling;

/// <summary>
/// Tratamento global: converte exceções em Problem Details (Constituição IV, research R-07).
/// Detalhes internos (mensagem/stack trace de erros inesperados) só aparecem em Development.
/// </summary>
public sealed class GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly HashSet<string> ConflictCodes =
    [
        ErrorCodes.AppointmentSlotUnavailable,
        ErrorCodes.AppointmentCustomerOverlap,
        ErrorCodes.AppointmentConcurrencyConflict,
        ErrorCodes.UserEmailTaken,
        ErrorCodes.ServiceNameTaken,
        ErrorCodes.ServiceInUse,
    ];

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Erro não tratado ao processar {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        }

        var problem = Problems.Create(httpContext, status, code, title, detail);
        if (status >= StatusCodes.Status500InternalServerError && environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.ToString();
        }

        await Problems.WriteAsync(httpContext, problem);
        return true;
    }

    /// <summary>Status HTTP de um <see cref="DomainException"/> a partir do seu código.</summary>
    public static int StatusForDomainCode(string code) => code switch
    {
        ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ErrorCodes.AuthForbidden => StatusCodes.Status403Forbidden,
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when ConflictCodes.Contains(code) => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private (int Status, string Code, string Title, string? Detail) Map(Exception exception) => exception switch
    {
        NotFoundException e => (StatusCodes.Status404NotFound, e.Code, "Recurso não encontrado.", e.Message),
        ConflictException e => (StatusCodes.Status409Conflict, e.Code, "Conflito de estado.", e.Message),
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, "Acesso negado.", e.Message),
        UnauthenticatedException e => (StatusCodes.Status401Unauthorized, e.Code, "Não autenticado.", e.Message),
        PreconditionFailedException e => (StatusCodes.Status412PreconditionFailed, e.Code, "Pré-condição falhou.", e.Message),
        BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Code, "Regra de negócio violada.", e.Message),
        DomainException e => (StatusForDomainCode(e.Code), e.Code, TitleFor(StatusForDomainCode(e.Code)), e.Message),
        BadHttpRequestException e => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Requisição inválida.", environment.IsDevelopment() ? e.Message : null),
        _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Erro interno do servidor.", environment.IsDevelopment() ? exception.Message : null),
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Um ou mais campos são inválidos.",
        StatusCodes.Status403Forbidden => "Acesso negado.",
        StatusCodes.Status404NotFound => "Recurso não encontrado.",
        StatusCodes.Status409Conflict => "Conflito de estado.",
        _ => "Regra de negócio violada.",
    };
}
