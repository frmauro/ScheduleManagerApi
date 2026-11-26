using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Notifications;

namespace Scheduling.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    Guid AppointmentId,
    NotificationType Type,
    NotificationChannel Channel,
    string Subject,
    string Body,
    NotificationStatus Status,
    int Attempts,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt)
{
    public static NotificationDto From(Notification n) =>
        new(n.Id, n.AppointmentId, n.Type, n.Channel, n.Subject, n.Body, n.Status, n.Attempts, n.ScheduledFor, n.SentAt, n.CreatedAt);
}

public sealed class NotificationQuery : PageQuery
{
    public NotificationType? Type { get; set; }
}

public sealed class AdminNotificationQuery : PageQuery
{
    public NotificationStatus? Status { get; set; }

    public Guid? AppointmentId { get; set; }
}

public sealed class NotificationQueryValidator : PageQueryValidator<NotificationQuery>
{
    public NotificationQueryValidator() => RuleFor(x => x.Type).IsInEnum();
}

public sealed class AdminNotificationQueryValidator : PageQueryValidator<AdminNotificationQuery>
{
    public AdminNotificationQueryValidator() => RuleFor(x => x.Status).IsInEnum();
}

/// <summary>Consulta das notificações simuladas (FR-027).</summary>
public sealed class NotificationQueryService(INotificationRepository notifications, ICurrentUser currentUser)
{
    public async Task<PagedResult<NotificationDto>> GetOwnAsync(NotificationQuery query, CancellationToken cancellationToken = default)
    {
        var result = await notifications.SearchAsync(new NotificationSearch(currentUser.UserId, null, query.Type, null), query, cancellationToken);
        return result.Map(NotificationDto.From);
    }

    public async Task<PagedResult<NotificationDto>> SearchAsync(AdminNotificationQuery query, CancellationToken cancellationToken = default)
    {
        var result = await notifications.SearchAsync(new NotificationSearch(null, query.Status, null, query.AppointmentId), query, cancellationToken);
        return result.Map(NotificationDto.From);
    }
}
