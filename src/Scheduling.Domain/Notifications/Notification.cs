using Scheduling.Domain.Common;

namespace Scheduling.Domain.Notifications;

public enum NotificationType
{
    AppointmentCreated,
    AppointmentConfirmed,
    AppointmentCancelled,
    AppointmentReminder,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Cancelled,
}

public enum NotificationChannel
{
    Email,
}

/// <summary>Registro de comunicação (outbox). Falhas de envio nunca afetam o agendamento (FR-027).</summary>
public sealed class Notification : AggregateRoot
{
    public const int SubjectMaxLength = 200;
    public const int LastErrorMaxLength = 1000;

    /// <summary>Backoff entre tentativas: 1, 5 e 25 minutos (research R-11).</summary>
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(25)];

    private Notification()
    {
        Subject = string.Empty;
        Body = string.Empty;
    }

    public Guid AppointmentId { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public NotificationType Type { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public static Notification Create(
        Guid appointmentId,
        Guid recipientUserId,
        NotificationType type,
        string subject,
        string body,
        DateTimeOffset scheduledFor)
    {
        return new Notification
        {
            AppointmentId = appointmentId,
            RecipientUserId = recipientUserId,
            Channel = NotificationChannel.Email,
            Type = type,
            Subject = subject.Length > SubjectMaxLength ? subject[..SubjectMaxLength] : subject,
            Body = body,
            Status = NotificationStatus.Pending,
            Attempts = 0,
            ScheduledFor = scheduledFor,
            NextAttemptAt = scheduledFor,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    public void MarkFailedAttempt(string error, DateTimeOffset now, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;

        if (Attempts >= maxAttempts)
        {
            Status = NotificationStatus.Failed;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = now + Backoff[Math.Min(Attempts - 1, Backoff.Length - 1)];
    }

    public void Cancel()
    {
        if (Status == NotificationStatus.Pending)
        {
            Status = NotificationStatus.Cancelled;
            NextAttemptAt = null;
        }
    }
}
