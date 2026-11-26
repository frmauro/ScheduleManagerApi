using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Notifications;

namespace Scheduling.Api.Controllers;

/// <summary>Notificações (simuladas) do usuário autenticado (FR-027).</summary>
[Route(RoutePrefix + "/notifications")]
[Authorize]
[Tags("Notifications")]
public sealed class NotificationsController(NotificationQueryService notifications) : ApiControllerBase
{
    /// <summary>Notificações do usuário autenticado, mais recentes primeiro.</summary>
    [HttpGet("me")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public Task<PagedResult<NotificationDto>> GetOwn([FromQuery] NotificationQuery query, CancellationToken cancellationToken) =>
        notifications.GetOwnAsync(query, cancellationToken);
}
