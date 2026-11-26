using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Services;

namespace Scheduling.Api.Controllers;

/// <summary>Serviços oferecidos pelos prestadores (FR-002).</summary>
[Route(RoutePrefix + "/providers")]
[Tags("Services")]
public sealed class ServicesController(ServiceCatalogService services) : ApiControllerBase
{
    /// <summary>Serviços ativos de um prestador (público).</summary>
    [HttpGet("{providerId:guid}/services")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ServiceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<PagedResult<ServiceDto>> ListPublic(Guid providerId, [FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        services.ListPublicAsync(providerId, page, cancellationToken);

    /// <summary>Todos os serviços do prestador autenticado (inclui inativos).</summary>
    [HttpGet("me/services")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<PagedResult<ServiceDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ServiceDto>> ListOwn([FromQuery] ServiceListQuery query, CancellationToken cancellationToken) =>
        services.ListOwnAsync(query, cancellationToken);

    /// <summary>Cadastra serviço.</summary>
    [HttpPost("me/services")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ServiceDto>> Create(ServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await services.CreateAsync(request, cancellationToken);
        return Created($"/{RoutePrefix}/providers/me/services/{service.Id}", service);
    }

    /// <summary>Atualiza serviço (agendamentos existentes mantêm o snapshot).</summary>
    [HttpPut("me/services/{serviceId:guid}")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<ServiceDto> Update(Guid serviceId, ServiceRequest request, CancellationToken cancellationToken) =>
        services.UpdateAsync(serviceId, request, cancellationToken);

    /// <summary>Exclui serviço nunca agendado; se já usado → 409 <c>service.in_use</c>.</summary>
    [HttpDelete("me/services/{serviceId:guid}")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid serviceId, CancellationToken cancellationToken)
    {
        await services.DeleteAsync(serviceId, cancellationToken);
        return NoContent();
    }

    /// <summary>Ativa/desativa serviço.</summary>
    [HttpPatch("me/services/{serviceId:guid}/status")]
    [Authorize(Policy = Policies.ProviderOnly)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ServiceDto> SetStatus(Guid serviceId, ActiveStatusRequest request, CancellationToken cancellationToken) =>
        services.SetStatusAsync(serviceId, request.IsActive, cancellationToken);
}
