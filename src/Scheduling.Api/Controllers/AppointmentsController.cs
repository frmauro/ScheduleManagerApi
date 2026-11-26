using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Appointments;

namespace Scheduling.Api.Controllers;

/// <summary>Criação e ciclo de vida de agendamentos (FR-015–FR-023).</summary>
[Route(RoutePrefix + "/appointments")]
[Authorize]
[Tags("Appointments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AppointmentsController(AppointmentService appointments) : ApiControllerBase
{
    /// <summary>
    /// Cria agendamento. Header opcional <c>Idempotency-Key</c> (UUID): repetição com o mesmo conteúdo retorna 200 com o
    /// agendamento original. Horário ocupado → 409.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CustomerOnly)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentDto>> Create(
        CreateAppointmentRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await appointments.CreateAsync(request, idempotencyKey, cancellationToken);
        SetETag(result.Appointment.Version);
        return result.IsReplay
            ? Ok(result.Appointment)
            : Created($"/{RoutePrefix}/appointments/{result.Appointment.Id}", result.Appointment);
    }

    /// <summary>Detalhe com histórico. Visível ao cliente dono, ao prestador e ao Admin; demais → 404.</summary>
    [HttpGet("{appointmentId:guid}")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<AppointmentDto> Get(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken);
        SetETag(appointment.Version);
        return appointment;
    }

    /// <summary>Pending → Confirmed (Prestador/Admin). Aceita <c>If-Match</c>.</summary>
    [HttpPost("{appointmentId:guid}/confirm")]
    [Authorize(Policy = Policies.ProviderOrAdmin)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<AppointmentDto> Confirm(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.ConfirmAsync(appointmentId, ReadIfMatch(), cancellationToken);
        SetETag(appointment.Version);
        return appointment;
    }

    /// <summary>
    /// Cancela. O status resultante deriva do perfil (Cliente → CancelledByCustomer; Prestador → CancelledByProvider;
    /// Admin informa <c>onBehalfOf</c>). Libera o horário imediatamente.
    /// </summary>
    [HttpPost("{appointmentId:guid}/cancel")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<AppointmentDto> Cancel(Guid appointmentId, CancelAppointmentRequest request, CancellationToken cancellationToken)
    {
        var appointment = await appointments.CancelAsync(appointmentId, request, ReadIfMatch(), cancellationToken);
        SetETag(appointment.Version);
        return appointment;
    }

    /// <summary>Confirmed → Completed, somente após o início (Prestador/Admin).</summary>
    [HttpPost("{appointmentId:guid}/complete")]
    [Authorize(Policy = Policies.ProviderOrAdmin)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<AppointmentDto> Complete(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.CompleteAsync(appointmentId, ReadIfMatch(), cancellationToken);
        SetETag(appointment.Version);
        return appointment;
    }
}
