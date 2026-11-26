namespace Scheduling.Domain.Common;

/// <summary>Códigos de erro estáveis e legíveis por máquina (extensão <c>code</c> do Problem Details).</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string InternalError = "server.internal_error";

    public const string AuthUnauthenticated = "auth.unauthenticated";
    public const string AuthInvalidCredentials = "auth.invalid_credentials";
    public const string AuthForbidden = "auth.forbidden";

    public const string UserEmailTaken = "user.email_taken";
    public const string UserNotFound = "user.not_found";

    public const string ProviderNotFound = "provider.not_found";
    public const string ProviderInactive = "provider.inactive";

    public const string ServiceNotFound = "service.not_found";
    public const string ServiceInactive = "service.inactive";
    public const string ServiceNameTaken = "service.name_taken";
    public const string ServiceInUse = "service.in_use";

    public const string ScheduleInvalidWorkingHours = "schedule.invalid_working_hours";
    public const string ScheduleInvalidException = "schedule.invalid_exception";
    public const string ScheduleExceptionInPast = "schedule.exception_in_past";
    public const string ScheduleSpecialHoursOverlap = "schedule.special_hours_overlap";
    public const string ScheduleExceptionNotFound = "schedule.exception_not_found";

    public const string AvailabilityDateInPast = "availability.date_in_past";
    public const string AvailabilityBeyondHorizon = "availability.beyond_horizon";
    public const string AvailabilityOutsideWorkingHours = "availability.outside_working_hours";
    public const string AvailabilityClosed = "availability.closed";
    public const string AvailabilityMisalignedStart = "availability.misaligned_start";
    public const string AvailabilityTooSoon = "availability.too_soon";

    public const string AppointmentNotFound = "appointment.not_found";
    public const string AppointmentSlotUnavailable = "appointment.slot_unavailable";
    public const string AppointmentCustomerOverlap = "appointment.customer_overlap";
    public const string AppointmentConcurrencyConflict = "appointment.concurrency_conflict";
    public const string AppointmentPreconditionFailed = "appointment.precondition_failed";
    public const string AppointmentInvalidTransition = "appointment.invalid_transition";
    public const string AppointmentCancellationWindow = "appointment.cancellation_window";
    public const string AppointmentAlreadyStarted = "appointment.already_started";
    public const string AppointmentNotStarted = "appointment.not_started";

    public const string IdempotencyKeyReused = "idempotency.key_reused";

    public const string AdminCannotDeactivateSelf = "admin.cannot_deactivate_self";
}
