using Scheduling.Domain.Availability;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;

namespace Scheduling.Domain.Appointments;

/// <summary>Parte em nome da qual um Admin cancela.</summary>
public enum CancellationParty
{
    Customer,
    Provider,
}

/// <summary>Agendamento: reserva de um horário com máquina de estados auditada (FR-015–FR-022).</summary>
public sealed class Appointment : AggregateRoot
{
    private readonly List<AppointmentStatusChange> _history = [];

    private Appointment()
    {
        Service = null!;
    }

    public Guid ProviderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid ServiceId { get; private set; }

    public ServiceSnapshot Service { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    /// <summary>Fim + intervalo de preparo (snapshot); limite superior da ocupação na agenda do prestador.</summary>
    public DateTimeOffset BlockedUntil { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public string? CustomerNotes { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid? IdempotencyKey { get; private set; }

    /// <summary>Token de concorrência otimista (mapeado para <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<AppointmentStatusChange> History => _history.AsReadOnly();

    public static Appointment Create(
        Guid providerId,
        Guid customerId,
        Guid serviceId,
        ServiceSnapshot service,
        TimeSlot slot,
        int bufferMinutes,
        bool autoConfirm,
        string? notes,
        Guid? idempotencyKey,
        Actor actor,
        DateTimeOffset now)
    {
        if (slot.EndAt <= slot.StartAt)
        {
            throw Guard.Invalid("O fim do agendamento deve ser posterior ao início.");
        }

        var appointment = new Appointment
        {
            ProviderId = providerId,
            CustomerId = customerId,
            ServiceId = serviceId,
            // Cópia própria: o snapshot é um value object e não deve ser compartilhado entre agendamentos.
            Service = service with { },
            StartAt = slot.StartAt.ToUniversalTime(),
            EndAt = slot.EndAt.ToUniversalTime(),
            BlockedUntil = slot.EndAt.ToUniversalTime().AddMinutes(bufferMinutes),
            Status = autoConfirm ? AppointmentStatus.Confirmed : AppointmentStatus.Pending,
            CustomerNotes = Guard.OptionalText(notes, "notes", ValidationLimits.NotesMaxLength),
            IdempotencyKey = idempotencyKey,
        };

        appointment._history.Add(AppointmentStatusChange.Create(appointment.Id, null, appointment.Status, actor, null, now));
        return appointment;
    }

    /// <summary>Pending → Confirmed (Prestador/Admin).</summary>
    public void Confirm(Actor actor, DateTimeOffset now)
    {
        EnsureRole(actor, Role.Provider, Role.Admin);
        if (Status != AppointmentStatus.Pending)
        {
            throw InvalidTransition(AppointmentStatus.Confirmed);
        }

        TransitionTo(AppointmentStatus.Confirmed, actor, null, now);
    }

    /// <summary>Pending|Confirmed → CancelledByCustomer|CancelledByProvider.</summary>
    public void Cancel(Actor actor, CancellationParty? onBehalfOf, string reason, int minCancellationNoticeMinutes, DateTimeOffset now)
    {
        var target = actor.Role switch
        {
            Role.Customer => AppointmentStatus.CancelledByCustomer,
            Role.Provider => AppointmentStatus.CancelledByProvider,
            Role.Admin => onBehalfOf switch
            {
                CancellationParty.Customer => AppointmentStatus.CancelledByCustomer,
                CancellationParty.Provider => AppointmentStatus.CancelledByProvider,
                _ => throw Guard.Invalid("Admin deve informar 'onBehalfOf' (Customer ou Provider)."),
            },
            _ => throw new DomainException(ErrorCodes.AuthForbidden, "Perfil sem permissão para cancelar."),
        };

        if (Status.IsFinal())
        {
            throw InvalidTransition(target);
        }

        if (now >= StartAt)
        {
            throw new DomainException(ErrorCodes.AppointmentAlreadyStarted, "Não é possível cancelar um agendamento que já começou.");
        }

        if (actor.Role == Role.Customer && now > StartAt.AddMinutes(-minCancellationNoticeMinutes))
        {
            throw new DomainException(
                ErrorCodes.AppointmentCancellationWindow,
                $"O cancelamento pelo cliente exige antecedência mínima de {minCancellationNoticeMinutes} minutos.");
        }

        CancellationReason = Guard.Text(reason, "reason", ValidationLimits.CancellationReasonMinLength, ValidationLimits.CancellationReasonMaxLength);
        TransitionTo(target, actor, CancellationReason, now);
    }

    /// <summary>Confirmed → Completed, somente após o início (Prestador/Admin).</summary>
    public void Complete(Actor actor, DateTimeOffset now)
    {
        EnsureRole(actor, Role.Provider, Role.Admin);
        if (Status != AppointmentStatus.Confirmed)
        {
            throw InvalidTransition(AppointmentStatus.Completed);
        }

        if (now < StartAt)
        {
            throw new DomainException(ErrorCodes.AppointmentNotStarted, "Só é possível concluir após o horário de início.");
        }

        TransitionTo(AppointmentStatus.Completed, actor, null, now);
    }

    /// <summary>Indica se a requisição de criação equivale à que gerou este agendamento (idempotência).</summary>
    public bool MatchesRequest(Guid providerId, Guid serviceId, DateTimeOffset startAt, string? notes) =>
        ProviderId == providerId
        && ServiceId == serviceId
        && StartAt == startAt.ToUniversalTime()
        && string.Equals(CustomerNotes, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), StringComparison.Ordinal);

    private static void EnsureRole(Actor actor, params Role[] allowed)
    {
        if (!allowed.Contains(actor.Role))
        {
            throw new DomainException(ErrorCodes.AuthForbidden, "Perfil sem permissão para esta ação.");
        }
    }

    private DomainException InvalidTransition(AppointmentStatus target) =>
        new(ErrorCodes.AppointmentInvalidTransition, $"Transição inválida: {Status} → {target}.");

    private void TransitionTo(AppointmentStatus target, Actor actor, string? reason, DateTimeOffset now)
    {
        var from = Status;
        Status = target;
        _history.Add(AppointmentStatusChange.Create(Id, from, target, actor, reason, now));
    }
}
