using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Schedule;

namespace Scheduling.Api.Controllers;

/// <summary>Expediente semanal e exceções do prestador autenticado (FR-003, FR-004, FR-007).</summary>
[Route(RoutePrefix + "/providers/me/schedule")]
[Authorize(Policy = Policies.ProviderOnly)]
[Tags("Schedule")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ScheduleController(ScheduleService schedule) : ApiControllerBase
{
    /// <summary>Expediente semanal.</summary>
    [HttpGet]
    [ProducesResponseType<WeeklyScheduleDto>(StatusCodes.Status200OK)]
    public Task<WeeklyScheduleDto> Get(CancellationToken cancellationToken) => schedule.GetAsync(cancellationToken);

    /// <summary>Substitui o expediente semanal; informa agendamentos ativos que ficaram fora (não os cancela).</summary>
    [HttpPut("working-hours")]
    [ProducesResponseType<ScheduleChangeResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ScheduleChangeResultDto> ReplaceWorkingHours(WeeklyScheduleDto request, CancellationToken cancellationToken) =>
        schedule.ReplaceWorkingHoursAsync(request, cancellationToken);

    /// <summary>Lista exceções (feriados, férias, horários especiais).</summary>
    [HttpGet("exceptions")]
    [ProducesResponseType<PagedResult<ScheduleExceptionDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ScheduleExceptionDto>> ListExceptions([FromQuery] ScheduleExceptionListQuery query, CancellationToken cancellationToken) =>
        schedule.ListExceptionsAsync(query, cancellationToken);

    /// <summary>Cria exceção; informa agendamentos ativos afetados (não os cancela).</summary>
    [HttpPost("exceptions")]
    [ProducesResponseType<ScheduleExceptionCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ScheduleExceptionCreatedDto>> CreateException(ScheduleExceptionRequest request, CancellationToken cancellationToken)
    {
        var result = await schedule.CreateExceptionAsync(request, cancellationToken);
        return Created($"/{RoutePrefix}/providers/me/schedule/exceptions/{result.Exception.Id}", result);
    }

    /// <summary>Remove exceção.</summary>
    [HttpDelete("exceptions/{exceptionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteException(Guid exceptionId, CancellationToken cancellationToken)
    {
        await schedule.DeleteExceptionAsync(exceptionId, cancellationToken);
        return NoContent();
    }
}
