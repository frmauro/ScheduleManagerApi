using Scheduling.Domain.Common;
using Scheduling.Domain.Users;

namespace Scheduling.Domain.Appointments;

/// <summary>Registro de auditoria de cada transição de status (FR-021).</summary>
public sealed class AppointmentStatusChange : Entity
{
    private AppointmentStatusChange()
    {
    }

    public Guid AppointmentId { get; private set; }

    public AppointmentStatus? FromStatus { get; private set; }

    public AppointmentStatus ToStatus { get; private set; }

    public Guid ChangedByUserId { get; private set; }

    public Role ChangedByRole { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    internal static AppointmentStatusChange Create(Guid appointmentId, AppointmentStatus? from, AppointmentStatus to, Actor actor, string? reason, DateTimeOffset now)
    {
        return new AppointmentStatusChange
        {
            AppointmentId = appointmentId,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actor.UserId,
            ChangedByRole = actor.Role,
            Reason = reason,
            ChangedAt = now,
        };
    }
}
