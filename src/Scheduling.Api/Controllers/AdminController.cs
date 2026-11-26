using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Auth;
using Scheduling.Application.Admin;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Notifications;
using Scheduling.Application.Services;

namespace Scheduling.Api.Controllers;

/// <summary>Administração de contas e auditoria de notificações (FR-010).</summary>
[Route(RoutePrefix + "/admin")]
[Authorize(Policy = Policies.AdminOnly)]
[Tags("Admin")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AdminController(AdminUserService users, NotificationQueryService notifications) : ApiControllerBase
{
    /// <summary>Lista usuários com filtros por perfil, situação e texto.</summary>
    [HttpGet("users")]
    [ProducesResponseType<PagedResult<UserSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<UserSummaryDto>> ListUsers([FromQuery] AdminUserQuery query, CancellationToken cancellationToken) =>
        users.SearchAsync(query, cancellationToken);

    /// <summary>Ativa/desativa conta (prestador inativo some do catálogo e não aceita reservas).</summary>
    [HttpPatch("users/{userId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> SetUserStatus(Guid userId, ActiveStatusRequest request, CancellationToken cancellationToken)
    {
        await users.SetStatusAsync(userId, request.IsActive, cancellationToken);
        return NoContent();
    }

    /// <summary>Auditoria do outbox de notificações.</summary>
    [HttpGet("notifications")]
    [Tags("Admin", "Notifications")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<NotificationDto>> ListNotifications([FromQuery] AdminNotificationQuery query, CancellationToken cancellationToken) =>
        notifications.SearchAsync(query, cancellationToken);
}
