using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Providers;

namespace Scheduling.Api.Controllers;

/// <summary>Catálogo público, perfil, política de reservas e agenda do prestador (FR-001, FR-005, FR-006, FR-024).</summary>
[Route(RoutePrefix + "/providers")]
[Tags("Providers")]
public sealed class ProvidersController(ProviderService providers, AppointmentService appointments) : ApiControllerBase
{
    /// <summary>Catálogo público de prestadores ativos.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ProviderSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<PagedResult<ProviderSummaryDto>> Search([FromQuery] ProviderSearchQuery query, CancellationToken cancellationToken) =>
        providers.SearchAsync(query, cancellationToken);

    /// <summary>Perfil público de um prestador ativo.</summary>
    [HttpGet("{providerId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ProviderPublicProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ProviderPublicProfileDto> GetPublic(Guid providerId, CancellationToken cancellationToken) =>
        providers.GetPublicAsync(providerId, cancellationToken);

    /// <summary>Perfil completo do prestador autenticado.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<ProviderProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<ProviderProfileDto> GetOwn(CancellationToken cancellationToken) => providers.GetOwnAsync(cancellationToken);

    /// <summary>Atualiza o perfil do prestador.</summary>
    [HttpPut("me")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<ProviderProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<ProviderProfileDto> UpdateOwn(UpdateProviderRequest request, CancellationToken cancellationToken) =>
        providers.UpdateOwnAsync(request, cancellationToken);

    /// <summary>Regras de reserva (vale para novas reservas; agendamentos existentes mantêm seu snapshot).</summary>
    [HttpPut("me/booking-policy")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<BookingPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<BookingPolicyDto> UpdateBookingPolicy(BookingPolicyDto request, CancellationToken cancellationToken) =>
        providers.UpdateBookingPolicyAsync(request, cancellationToken);

    /// <summary>Agenda do prestador com filtros por período, status e serviço.</summary>
    [HttpGet("me/appointments")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [Tags("Providers", "Appointments")]
    [ProducesResponseType<PagedResult<AppointmentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<PagedResult<AppointmentSummaryDto>> Agenda([FromQuery] AgendaQuery query, CancellationToken cancellationToken) =>
        appointments.GetProviderAgendaAsync(query, cancellationToken);
}
