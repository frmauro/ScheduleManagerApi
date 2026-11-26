using FluentValidation;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Common.Validation;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;

namespace Scheduling.Application.Schedule;

/// <summary>Faixa em hora local de parede ("HH:mm").</summary>
public sealed record TimeIntervalDto(string Start, string End);

public sealed record DayScheduleDto(DayOfWeek DayOfWeek, IReadOnlyList<TimeIntervalDto> Intervals);

public sealed record WeeklyScheduleDto(string? TimeZoneId, IReadOnlyList<DayScheduleDto> Days)
{
    public static WeeklyScheduleDto From(Provider provider) =>
        new(
            provider.TimeZoneId,
            provider.WorkingHours
                .GroupBy(w => w.DayOfWeek)
                .OrderBy(g => g.Key)
                .Select(g => new DayScheduleDto(
                    g.Key,
                    g.OrderBy(w => w.StartTime)
                        .Select(w => new TimeIntervalDto(RuleExtensions.FormatLocalTime(w.StartTime), RuleExtensions.FormatLocalTime(w.EndTime)))
                        .ToList()))
                .ToList());
}

public sealed record ScheduleExceptionRequest(
    ScheduleExceptionType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? StartTime,
    string? EndTime,
    string? Reason);

public sealed record ScheduleExceptionDto(
    Guid Id,
    ScheduleExceptionType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? StartTime,
    string? EndTime,
    string? Reason)
{
    public static ScheduleExceptionDto From(ScheduleException e) =>
        new(
            e.Id,
            e.Type,
            e.StartDate,
            e.EndDate,
            e.StartTime is { } s ? RuleExtensions.FormatLocalTime(s) : null,
            e.EndTime is { } t ? RuleExtensions.FormatLocalTime(t) : null,
            e.Reason);
}

public sealed record ScheduleChangeResultDto(WeeklyScheduleDto Schedule, IReadOnlyList<AppointmentSummaryDto> AffectedAppointments);

public sealed record ScheduleExceptionCreatedDto(ScheduleExceptionDto Exception, IReadOnlyList<AppointmentSummaryDto> AffectedAppointments);

public sealed class ScheduleExceptionListQuery : PageQuery
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public sealed class TimeIntervalDtoValidator : AbstractValidator<TimeIntervalDto>
{
    public TimeIntervalDtoValidator()
    {
        RuleFor(x => x.Start).NotEmpty().LocalTime();
        RuleFor(x => x.End).NotEmpty().LocalTime();
        RuleFor(x => x)
            .Must(x => RuleExtensions.ParseLocalTime(x.Start) < RuleExtensions.ParseLocalTime(x.End))
            .When(x => RuleExtensions.TryParseLocalTime(x.Start ?? string.Empty, out _) && RuleExtensions.TryParseLocalTime(x.End ?? string.Empty, out _))
            .WithName("end")
            .WithMessage("'start' deve ser anterior a 'end' (a faixa não pode atravessar a meia-noite).");
    }
}

public sealed class WeeklyScheduleDtoValidator : AbstractValidator<WeeklyScheduleDto>
{
    public WeeklyScheduleDtoValidator()
    {
        RuleFor(x => x.Days)
            .NotNull()
            .Must(d => d.Count <= 7).WithMessage("'days' deve ter no máximo 7 itens.")
            .Must(d => d.Select(x => x.DayOfWeek).Distinct().Count() == d.Count).WithMessage("Cada 'dayOfWeek' deve aparecer uma única vez.");
        RuleForEach(x => x.Days).NotNull().ChildRules(day =>
        {
            day.RuleFor(d => d.DayOfWeek).IsInEnum();
            day.RuleFor(d => d.Intervals)
                .NotEmpty()
                .Must(i => i.Count <= ValidationLimits.MaxIntervalsPerDay)
                .WithMessage($"Máximo de {ValidationLimits.MaxIntervalsPerDay} faixas por dia.");
            day.RuleForEach(d => d.Intervals).SetValidator(new TimeIntervalDtoValidator());
            day.RuleFor(d => d.Intervals)
                .Must(NotOverlap)
                .When(d => d.Intervals is not null && d.Intervals.All(IsParseable))
                .WithMessage("As faixas do mesmo dia não podem se sobrepor.");
        });
    }

    private static bool IsParseable(TimeIntervalDto i) =>
        RuleExtensions.TryParseLocalTime(i.Start ?? string.Empty, out var s)
        && RuleExtensions.TryParseLocalTime(i.End ?? string.Empty, out var e)
        && s < e;

    private static bool NotOverlap(IReadOnlyList<TimeIntervalDto> intervals)
    {
        var ranges = intervals
            .Select(i => (Start: RuleExtensions.ParseLocalTime(i.Start), End: RuleExtensions.ParseLocalTime(i.End)))
            .OrderBy(r => r.Start)
            .ToList();
        return ranges.Zip(ranges.Skip(1)).All(pair => pair.First.End <= pair.Second.Start);
    }
}

public sealed class ScheduleExceptionRequestValidator : AbstractValidator<ScheduleExceptionRequest>
{
    public ScheduleExceptionRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).WithMessage("'endDate' deve ser igual ou posterior a 'startDate'.");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end.DayNumber - x.StartDate.DayNumber + 1 <= ValidationLimits.ExceptionMaxPeriodDays)
            .WithMessage($"O período deve ter no máximo {ValidationLimits.ExceptionMaxPeriodDays} dias.");
        RuleFor(x => x.StartTime).LocalTime();
        RuleFor(x => x.EndTime).LocalTime();
        RuleFor(x => x.EndTime)
            .Must((x, end) => (x.StartTime is null) == (end is null))
            .WithMessage("'startTime' e 'endTime' devem ser ambos informados ou ambos omitidos.");
        RuleFor(x => x.StartTime)
            .NotEmpty()
            .When(x => x.Type == ScheduleExceptionType.SpecialHours)
            .WithMessage("Horário especial exige 'startTime' e 'endTime'.");
        RuleFor(x => x.EndTime)
            .Must((x, end) => RuleExtensions.ParseLocalTime(x.StartTime!) < RuleExtensions.ParseLocalTime(end!))
            .When(x => x.StartTime is not null && x.EndTime is not null
                && RuleExtensions.TryParseLocalTime(x.StartTime, out _) && RuleExtensions.TryParseLocalTime(x.EndTime, out _))
            .WithMessage("'startTime' deve ser anterior a 'endTime'.");
        RuleFor(x => x.Reason).MaximumLength(ValidationLimits.ExceptionReasonMaxLength);
    }
}

public sealed class ScheduleExceptionListQueryValidator : PageQueryValidator<ScheduleExceptionListQuery>
{
    public ScheduleExceptionListQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}
