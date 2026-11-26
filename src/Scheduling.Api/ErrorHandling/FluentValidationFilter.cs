using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Scheduling.Api.ErrorHandling;

/// <summary>
/// Executa o <see cref="IValidator{T}"/> registrado para cada argumento de action (Constituição IV) e responde
/// 400 <c>validation.failed</c> com os erros por campo em camelCase.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                var key = Problems.ToJsonPath(failure.PropertyName);
                if (!errors.TryGetValue(key, out var list))
                {
                    errors[key] = list = [];
                }

                list.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            var problem = Problems.Validation(context.HttpContext, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
            context.Result = ToResult(problem);
            return;
        }

        await next();
    }

    /// <summary>Resposta para erros de model binding (JSON malformado, tipos inválidos, parâmetros de query inválidos).</summary>
    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => Problems.ToJsonPath(entry.Key))
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(entry => entry.Value!.Errors)
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Valor inválido." : error.ErrorMessage)
                    .ToArray());

        return ToResult(Problems.Validation(context.HttpContext, errors));
    }

    /// <summary>JsonResult (e não ObjectResult) para que [Produces] não sobrescreva o content-type problem+json.</summary>
    private static JsonResult ToResult(ValidationProblemDetails problem) =>
        new(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentType = Problems.ContentType };
}
