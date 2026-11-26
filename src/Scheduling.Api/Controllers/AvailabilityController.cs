using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Availability;

namespace Scheduling.Api.Controllers;

/// <summary>Consulta de horários livres (FR-014, matriz R1–R10).</summary>
[Route(RoutePrefix + "/providers")]
[Tags("Availability")]
public sealed class AvailabilityController(AvailabilityService availability) : ApiControllerBase
{
    /// <summary>
    /// Horários de início livres para um serviço em uma data (fuso do prestador). A lista não reserva horários;
    /// dia sem expediente/fechado → <c>slots: []</c>.
    /// </summary>
    [HttpGet("{providerId:guid}/availability")]
    [AllowAnonymous]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<AvailabilityResponse> Get(Guid providerId, [FromQuery] AvailabilityQuery query, CancellationToken cancellationToken) =>
        availability.GetAsync(providerId, query, cancellationToken);
}
