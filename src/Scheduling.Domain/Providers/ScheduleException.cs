using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

public enum ScheduleExceptionType
{
    /// <summary>Fechamento (feriado, férias, folga) — dia inteiro ou faixa parcial.</summary>
    Closure,

    /// <summary>Horário especial que substitui o expediente semanal na(s) data(s).</summary>
    SpecialHours,
}

/// <summary>Exceção de agenda por data ou período (parte do agregado Provider).</summary>
public sealed class ScheduleException : Entity
{
    private ScheduleException()
    {
    }

    public Guid ProviderId { get; private set; }

    public ScheduleExceptionType Type { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    public string? Reason { get; private set; }

    public bool IsFullDay => StartTime is null;

    public TimeRange? Range => StartTime is { } start && EndTime is { } end ? new TimeRange(start, end) : null;

    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;

    public bool OverlapsPeriod(DateOnly start, DateOnly end) => StartDate <= end && start <= EndDate;

    internal static ScheduleException Create(
        Guid providerId,
        ScheduleExceptionType type,
        DateOnly startDate,
        DateOnly endDate,
        TimeOnly? startTime,
        TimeOnly? endTime,
        string? reason,
        DateOnly today)
    {
        if (endDate < startDate)
        {
            throw new DomainException(ErrorCodes.ScheduleInvalidException, "'endDate' deve ser igual ou posterior a 'startDate'.");
        }

        if (endDate.DayNumber - startDate.DayNumber + 1 > ValidationLimits.ExceptionMaxPeriodDays)
        {
            throw new DomainException(ErrorCodes.ScheduleInvalidException, $"O período da exceção deve ter no máximo {ValidationLimits.ExceptionMaxPeriodDays} dias.");
        }

        if (startDate < today)
        {
            throw new DomainException(ErrorCodes.ScheduleExceptionInPast, "Não é possível criar exceções para datas passadas.");
        }

        if (startTime.HasValue != endTime.HasValue)
        {
            throw new DomainException(ErrorCodes.ScheduleInvalidException, "'startTime' e 'endTime' devem ser ambos informados ou ambos omitidos.");
        }

        if (type == ScheduleExceptionType.SpecialHours && startTime is null)
        {
            throw new DomainException(ErrorCodes.ScheduleInvalidException, "Horário especial exige 'startTime' e 'endTime'.");
        }

        if (startTime is { } s && endTime is { } e && s >= e)
        {
            throw new DomainException(ErrorCodes.ScheduleInvalidException, "'startTime' deve ser anterior a 'endTime'.");
        }

        return new ScheduleException
        {
            ProviderId = providerId,
            Type = type,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = startTime,
            EndTime = endTime,
            Reason = Guard.OptionalText(reason, "reason", ValidationLimits.ExceptionReasonMaxLength),
        };
    }
}
