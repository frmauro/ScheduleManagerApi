using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

/// <summary>
/// Prestador individual com uma única agenda (FR-033). Raiz do agregado que inclui o expediente semanal
/// (<see cref="WorkingHours"/>) e as exceções (<see cref="ScheduleException"/>).
/// </summary>
public sealed class Provider : AggregateRoot
{
    private readonly List<WorkingHours> _workingHours = [];
    private readonly List<ScheduleException> _exceptions = [];

    private Provider()
    {
        DisplayName = string.Empty;
        ContactEmail = string.Empty;
        ContactPhone = string.Empty;
        TimeZoneId = ValidationLimits.DefaultTimeZoneId;
        Address = null!;
        BookingPolicy = BookingPolicy.Default;
    }

    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; }

    public string? Description { get; private set; }

    public ServiceCategory Category { get; private set; }

    public string ContactEmail { get; private set; }

    public string ContactPhone { get; private set; }

    public Address Address { get; private set; }

    public string TimeZoneId { get; private set; }

    public BookingPolicy BookingPolicy { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<WorkingHours> WorkingHours => _workingHours.AsReadOnly();

    /// <summary>Exceções carregadas (o repositório pode carregar apenas um recorte de datas).</summary>
    public IReadOnlyCollection<ScheduleException> Exceptions => _exceptions.AsReadOnly();

    public static Provider Create(
        Guid userId,
        string displayName,
        string? description,
        ServiceCategory category,
        string contactEmail,
        string contactPhone,
        Address address,
        string? timeZoneId)
    {
        var provider = new Provider { UserId = userId, IsActive = true };
        provider.UpdateProfile(displayName, description, category, contactEmail, contactPhone, address, timeZoneId ?? ValidationLimits.DefaultTimeZoneId);
        return provider;
    }

    public static bool IsValidTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    public void UpdateProfile(
        string displayName,
        string? description,
        ServiceCategory category,
        string contactEmail,
        string contactPhone,
        Address address,
        string timeZoneId)
    {
        if (!IsValidTimeZone(timeZoneId) || timeZoneId.Length > ValidationLimits.TimeZoneIdMaxLength)
        {
            throw Guard.Invalid($"'timeZoneId' inválido: '{timeZoneId}'. Use um identificador IANA (ex.: America/Sao_Paulo).");
        }

        DisplayName = Guard.Text(displayName, "displayName", ValidationLimits.NameMinLength, ValidationLimits.NameMaxLength);
        Description = Guard.OptionalText(description, "description", ValidationLimits.ProviderDescriptionMaxLength);
        Category = category;
        ContactEmail = Guard.Email(contactEmail, "contactEmail");
        ContactPhone = Guard.Phone(contactPhone, "contactPhone");
        Address = address;
        TimeZoneId = timeZoneId;
    }

    public void UpdateBookingPolicy(BookingPolicy policy) => BookingPolicy = policy;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>Data local atual no fuso do prestador.</summary>
    public DateOnly GetLocalToday(DateTimeOffset utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, GetTimeZone()).DateTime);

    /// <summary>Substitui todo o expediente semanal (FR-003).</summary>
    public void ReplaceWorkingHours(IEnumerable<(DayOfWeek Day, TimeRange Range)> intervals)
    {
        var items = intervals.ToList();
        foreach (var dayGroup in items.GroupBy(i => i.Day))
        {
            var ranges = dayGroup.Select(i => i.Range).OrderBy(r => r.Start).ToList();
            if (ranges.Count > ValidationLimits.MaxIntervalsPerDay)
            {
                throw new DomainException(ErrorCodes.ScheduleInvalidWorkingHours, $"Máximo de {ValidationLimits.MaxIntervalsPerDay} faixas por dia ({dayGroup.Key}).");
            }

            for (var i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Duration.TotalMinutes < BookingPolicy.SlotStepMinutes)
                {
                    throw new DomainException(
                        ErrorCodes.ScheduleInvalidWorkingHours,
                        $"A faixa {ranges[i].Start:HH\\:mm}–{ranges[i].End:HH\\:mm} ({dayGroup.Key}) é menor que o passo da grade ({BookingPolicy.SlotStepMinutes} min).");
                }

                if (i > 0 && ranges[i - 1].Overlaps(ranges[i]))
                {
                    throw new DomainException(
                        ErrorCodes.ScheduleInvalidWorkingHours,
                        $"As faixas {ranges[i - 1].Start:HH\\:mm}–{ranges[i - 1].End:HH\\:mm} e {ranges[i].Start:HH\\:mm}–{ranges[i].End:HH\\:mm} ({dayGroup.Key}) se sobrepõem.");
                }
            }
        }

        _workingHours.Clear();
        _workingHours.AddRange(items.Select(i => Providers.WorkingHours.Create(Id, i.Day, i.Range)));
    }

    /// <summary>Cria exceção (FR-004). Exige que as exceções que se sobrepõem ao período estejam carregadas.</summary>
    public ScheduleException AddException(
        ScheduleExceptionType type,
        DateOnly startDate,
        DateOnly endDate,
        TimeOnly? startTime,
        TimeOnly? endTime,
        string? reason,
        DateOnly today)
    {
        var exception = ScheduleException.Create(Id, type, startDate, endDate, startTime, endTime, reason, today);

        if (type == ScheduleExceptionType.SpecialHours)
        {
            var newRange = exception.Range!.Value;
            var conflict = _exceptions.Any(e =>
                e.Type == ScheduleExceptionType.SpecialHours
                && e.OverlapsPeriod(startDate, endDate)
                && e.Range!.Value.Overlaps(newRange));

            if (conflict)
            {
                throw new DomainException(ErrorCodes.ScheduleSpecialHoursOverlap, "Já existe horário especial sobreposto nessa(s) data(s).");
            }
        }

        _exceptions.Add(exception);
        return exception;
    }

    public void RemoveException(Guid exceptionId)
    {
        var exception = _exceptions.FirstOrDefault(e => e.Id == exceptionId)
            ?? throw new DomainException(ErrorCodes.ScheduleExceptionNotFound, "Exceção não encontrada.");
        _exceptions.Remove(exception);
    }

    /// <summary>
    /// Indica se o atendimento [start, end) cabe inteiramente em uma faixa aberta da agenda atual (usado no FR-007
    /// para apontar agendamentos afetados por mudanças de expediente/exceções).
    /// </summary>
    public bool IsWithinSchedule(DateTimeOffset start, DateTimeOffset end)
    {
        var local = TimeZoneInfo.ConvertTime(start, GetTimeZone()).DateTime;
        var date = DateOnly.FromDateTime(local);
        return GetDaySchedule(date).OpenIntervals.Any(r => r.Contains(TimeOnly.FromDateTime(local), end - start));
    }

    /// <summary>Agenda efetiva de uma data local: R3 (horário especial substitui o semanal) e R4 (fechamentos).</summary>
    public DaySchedule GetDaySchedule(DateOnly date)
    {
        var special = _exceptions
            .Where(e => e.Type == ScheduleExceptionType.SpecialHours && e.Covers(date))
            .Select(e => e.Range!.Value)
            .ToList();

        var baseIntervals = special.Count > 0
            ? special
            : _workingHours.Where(w => w.DayOfWeek == date.DayOfWeek).Select(w => w.Range).ToList();

        var closures = _exceptions.Where(e => e.Type == ScheduleExceptionType.Closure && e.Covers(date)).ToList();
        var closedAllDay = closures.Any(c => c.IsFullDay);
        var partial = closures.Where(c => !c.IsFullDay).Select(c => c.Range!.Value).ToList();

        return new DaySchedule(date, baseIntervals.OrderBy(r => r.Start).ToList(), closedAllDay, partial);
    }
}
