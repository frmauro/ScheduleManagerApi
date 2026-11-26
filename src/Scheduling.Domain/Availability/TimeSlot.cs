namespace Scheduling.Domain.Availability;

/// <summary>Horário de início/fim (instantes) de um atendimento.</summary>
public readonly record struct TimeSlot(DateTimeOffset StartAt, DateTimeOffset EndAt);

/// <summary>Ocupação de agenda por um agendamento bloqueante: [StartAt, BlockedUntil).</summary>
public readonly record struct Occupation(DateTimeOffset StartAt, DateTimeOffset BlockedUntil);
