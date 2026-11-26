using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Customers;

namespace Scheduling.Api.Controllers;

/// <summary>Perfil e histórico do cliente autenticado (FR-012).</summary>
[Route(RoutePrefix + "/customers/me")]
[Authorize(Policy = Policies.CustomerOnly)]
[Tags("Customers")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class CustomersController(CustomerService customers) : ApiControllerBase
{
    /// <summary>Perfil do cliente autenticado.</summary>
    [HttpGet]
    [ProducesResponseType<CustomerProfileDto>(StatusCodes.Status200OK)]
    public Task<CustomerProfileDto> Get(CancellationToken cancellationToken) => customers.GetOwnAsync(cancellationToken);

    /// <summary>Atualiza nome, e-mail e telefone (e-mail de login acompanha a alteração).</summary>
    [HttpPut]
    [ProducesResponseType<CustomerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<CustomerProfileDto> Update(UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        customers.UpdateOwnAsync(request, cancellationToken);

    /// <summary>Histórico de agendamentos (padrão: mais recentes primeiro).</summary>
    [HttpGet("appointments")]
    [ProducesResponseType<PagedResult<AppointmentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<PagedResult<AppointmentSummaryDto>> History([FromQuery] CustomerHistoryQuery query, CancellationToken cancellationToken) =>
        customers.GetHistoryAsync(query, cancellationToken);
}
