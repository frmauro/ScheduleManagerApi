using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Notifications;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Notifications;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure.Notifications;

/// <summary>Canal simulado: apenas registra em log (sem PII além do id do destinatário) e marca como enviada.</summary>
internal sealed class SimulatedNotificationChannel(IOptions<NotificationOptions> options, ILogger<SimulatedNotificationChannel> logger) : INotificationChannel
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (options.Value.SimulateFailure)
        {
            throw new InvalidOperationException("Falha simulada do canal de notificação.");
        }

        logger.LogInformation(
            "Notificação simulada {NotificationId} ({Type}) enviada por {Channel} ao usuário {RecipientUserId}.",
            notification.Id,
            notification.Type,
            notification.Channel,
            notification.RecipientUserId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gera lembretes 24h antes para agendamentos Confirmed reservados com ≥ 24h de antecedência (FR-026).
/// A unicidade é garantida por <c>ux_notifications_reminder</c> com <c>ON CONFLICT DO NOTHING</c>.
/// </summary>
internal sealed class ReminderProcessor(SchedulingDbContext db, TimeProvider timeProvider) : IReminderProcessor
{
    private static readonly TimeSpan Lead = TimeSpan.FromHours(24);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var windowEnd = now + Lead;

        var due = await (
            from a in db.Appointments.AsNoTracking()
            where a.Status == AppointmentStatus.Confirmed
                && a.StartAt > now
                && a.StartAt <= windowEnd
                && a.CreatedAt <= a.StartAt.AddHours(-24)
                && !db.Notifications.Any(n => n.AppointmentId == a.Id && n.Type == NotificationType.AppointmentReminder)
            join p in db.Providers.AsNoTracking() on a.ProviderId equals p.Id
            join c in db.Customers.AsNoTracking() on a.CustomerId equals c.Id
            select new { Appointment = a, Provider = p, Customer = c })
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var item in due)
        {
            var n = NotificationFactory.AppointmentReminder(item.Appointment, item.Provider, item.Customer, now);
            created += await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO notifications (id, appointment_id, recipient_user_id, channel, type, subject, body, status, attempts, scheduled_for, next_attempt_at, created_at)
                VALUES ({n.Id}, {n.AppointmentId}, {n.RecipientUserId}, {n.Channel.ToString()}, {n.Type.ToString()}, {n.Subject}, {n.Body}, {n.Status.ToString()}, 0, {n.ScheduledFor}, {n.NextAttemptAt}, {now})
                ON CONFLICT (appointment_id, recipient_user_id) WHERE type = 'AppointmentReminder' DO NOTHING
                """,
                cancellationToken);
        }

        return created;
    }
}

/// <summary>Envia notificações pendentes (FOR UPDATE SKIP LOCKED), com backoff e limite de tentativas.</summary>
internal sealed class NotificationDispatcher(
    SchedulingDbContext db,
    INotificationChannel channel,
    IOptions<NotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.Notifications
            .FromSqlInterpolated(
                $"""
                SELECT * FROM notifications
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var notification in batch)
        {
            if (notification.Type == NotificationType.AppointmentReminder)
            {
                var status = await db.Appointments.Where(a => a.Id == notification.AppointmentId).Select(a => a.Status).FirstAsync(cancellationToken);
                if (status != AppointmentStatus.Confirmed)
                {
                    notification.Cancel();
                    continue;
                }
            }

            try
            {
                await channel.SendAsync(notification, cancellationToken);
                notification.MarkSent(now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falha ao enviar notificação {NotificationId} (tentativa {Attempt}).", notification.Id, notification.Attempts + 1);
                notification.MarkFailedAttempt(ex.Message, now, settings.MaxAttempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }
}
