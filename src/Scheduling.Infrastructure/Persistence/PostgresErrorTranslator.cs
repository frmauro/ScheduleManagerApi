using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Common;

namespace Scheduling.Infrastructure.Persistence;

/// <summary>Traduz violações de constraints do PostgreSQL em exceções de aplicação (research R-01, R-03).</summary>
internal static class PostgresErrorTranslator
{
    public const string UniqueViolation = "23505";
    public const string ExclusionViolation = "23P01";

    public static AppException? Translate(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException pg)
        {
            return null;
        }

        return (pg.SqlState, pg.ConstraintName) switch
        {
            (ExclusionViolation, Constraints.ProviderOverlap) =>
                new ConflictException(ErrorCodes.AppointmentSlotUnavailable, "O horário não está mais disponível para este prestador."),
            (ExclusionViolation, Constraints.CustomerOverlap) =>
                new ConflictException(ErrorCodes.AppointmentCustomerOverlap, "Você já possui um agendamento ativo que se sobrepõe a este horário."),
            (UniqueViolation, Constraints.UsersEmail) =>
                new ConflictException(ErrorCodes.UserEmailTaken, "Já existe uma conta com este e-mail."),
            (UniqueViolation, Constraints.ServicesProviderName) =>
                new ConflictException(ErrorCodes.ServiceNameTaken, "Já existe um serviço com este nome."),
            (UniqueViolation, Constraints.AppointmentsIdempotency) =>
                new ConflictException(ErrorCodes.IdempotencyKeyReused, "Requisição duplicada em processamento com a mesma chave de idempotência."),
            _ => null,
        };
    }
}

/// <summary>Nomes de constraints/índices referenciados em código e migrations.</summary>
internal static class Constraints
{
    public const string ProviderOverlap = "ex_appointments_provider_overlap";
    public const string CustomerOverlap = "ex_appointments_customer_overlap";
    public const string AppointmentsTime = "ck_appointments_time";
    public const string UsersEmail = "ux_users_email";
    public const string ServicesProviderName = "ux_services_provider_name";
    public const string AppointmentsIdempotency = "ux_appointments_idempotency";
    public const string NotificationsReminder = "ux_notifications_reminder";
}
