namespace Scheduling.Domain.Appointments;

public enum AppointmentStatus
{
    Pending,
    Confirmed,
    CancelledByCustomer,
    CancelledByProvider,
    Completed,
}

public static class AppointmentStatusExtensions
{
    /// <summary>Status que ocupam o horário na agenda (R6).</summary>
    public static bool IsBlocking(this AppointmentStatus status) =>
        status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;

    public static bool IsFinal(this AppointmentStatus status) => !status.IsBlocking();
}
