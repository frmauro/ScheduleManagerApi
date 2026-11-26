using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;

namespace Scheduling.Domain.Availability;

/// <summary>
/// Motor de disponibilidade: implementa a Matriz de Regras R1–R11 da spec. Puro (sem I/O) e usado tanto na consulta
/// quanto na criação de agendamentos, garantindo que ambos concordem (SC-004).
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>Horários de início livres para <paramref name="date"/> (data local do prestador).</summary>
    public static IReadOnlyList<TimeSlot> GetAvailableSlots(
        Provider provider,
        Service service,
        DateOnly date,
        IEnumerable<Occupation> occupations,
        DateTimeOffset now)
    {
        EnsureBookableEntities(provider, service);
        EnsureDateInRange(provider, date, now);

        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var buffer = TimeSpan.FromMinutes(provider.BookingPolicy.BufferMinutes);
        var earliest = now.AddMinutes(provider.BookingPolicy.MinBookingNoticeMinutes);
        var occupied = occupations.ToList();

        return BuildCandidates(provider, duration, date)
            .Where(slot => slot.StartAt >= earliest)
            .Where(slot => !Overlaps(slot.StartAt, slot.EndAt + buffer, occupied))
            .DistinctBy(slot => slot.StartAt.UtcTicks)
            .OrderBy(slot => slot.StartAt)
            .ToList();
    }

    /// <summary>
    /// Valida que <paramref name="requestedStart"/> é reservável (R1–R11) e devolve o slot; caso contrário lança
    /// <see cref="DomainException"/> com o motivo específico.
    /// </summary>
    public static TimeSlot EnsureBookable(
        Provider provider,
        Service service,
        DateTimeOffset requestedStart,
        IEnumerable<Occupation> occupations,
        DateTimeOffset now)
    {
        EnsureBookableEntities(provider, service);

        var timeZone = provider.GetTimeZone();
        var local = TimeZoneInfo.ConvertTime(requestedStart, timeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        EnsureDateInRange(provider, date, now);

        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var candidate = BuildCandidates(provider, duration, date).FirstOrDefault(slot => slot.StartAt == requestedStart);

        if (candidate == default)
        {
            throw new DomainException(ExplainRejection(provider, date, TimeOnly.FromDateTime(local), duration), RejectionMessage(requestedStart, timeZone));
        }

        if (candidate.StartAt < now.AddMinutes(provider.BookingPolicy.MinBookingNoticeMinutes))
        {
            throw new DomainException(
                ErrorCodes.AvailabilityTooSoon,
                $"Reservas exigem antecedência mínima de {provider.BookingPolicy.MinBookingNoticeMinutes} minutos.");
        }

        var buffer = TimeSpan.FromMinutes(provider.BookingPolicy.BufferMinutes);
        if (Overlaps(candidate.StartAt, candidate.EndAt + buffer, occupations.ToList()))
        {
            throw new DomainException(ErrorCodes.AppointmentSlotUnavailable, $"O horário {Format(requestedStart, timeZone)} não está mais disponível para este prestador.");
        }

        return candidate;
    }

    /// <summary>R1: prestador e serviço ativos e serviço pertencente ao prestador.</summary>
    private static void EnsureBookableEntities(Provider provider, Service service)
    {
        if (service.ProviderId != provider.Id)
        {
            throw new DomainException(ErrorCodes.ServiceNotFound, "Serviço não encontrado para este prestador.");
        }

        if (!provider.IsActive)
        {
            throw new DomainException(ErrorCodes.ProviderInactive, "O prestador não está aceitando reservas.");
        }

        if (!service.IsActive)
        {
            throw new DomainException(ErrorCodes.ServiceInactive, "O serviço não está disponível para reserva.");
        }
    }

    /// <summary>R2: data ∈ [hoje, hoje + horizonte] no fuso do prestador.</summary>
    private static void EnsureDateInRange(Provider provider, DateOnly date, DateTimeOffset now)
    {
        var today = provider.GetLocalToday(now);
        if (date < today)
        {
            throw new DomainException(ErrorCodes.AvailabilityDateInPast, "A data informada está no passado.");
        }

        if (date > today.AddDays(provider.BookingPolicy.BookingHorizonDays))
        {
            throw new DomainException(
                ErrorCodes.AvailabilityBeyondHorizon,
                $"Reservas são permitidas até {provider.BookingPolicy.BookingHorizonDays} dias à frente.");
        }
    }

    /// <summary>R3–R5 e R9: inícios alinhados à grade dentro das faixas abertas, convertidos para instantes.</summary>
    private static IEnumerable<TimeSlot> BuildCandidates(Provider provider, TimeSpan duration, DateOnly date)
    {
        var timeZone = provider.GetTimeZone();
        var step = TimeSpan.FromMinutes(provider.BookingPolicy.SlotStepMinutes);

        foreach (var interval in provider.GetDaySchedule(date).OpenIntervals)
        {
            var end = interval.End.ToTimeSpan();
            for (var start = interval.Start.ToTimeSpan(); start + duration <= end; start += step)
            {
                var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(start), DateTimeKind.Unspecified);
                if (ToInstant(localStart, timeZone) is { } instant)
                {
                    yield return new TimeSlot(instant, instant + duration);
                }
            }
        }
    }

    /// <summary>R9: hora inexistente (salto de horário de verão) → null; ambígua → primeiro deslocamento (o maior).</summary>
    private static DateTimeOffset? ToInstant(DateTime local, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset);
    }

    /// <summary>R6/R7: intervalos semiabertos; adjacência permitida.</summary>
    private static bool Overlaps(DateTimeOffset start, DateTimeOffset blockedUntil, IReadOnlyCollection<Occupation> occupations) =>
        occupations.Any(o => start < o.BlockedUntil && o.StartAt < blockedUntil);

    private static string ExplainRejection(Provider provider, DateOnly date, TimeOnly start, TimeSpan duration)
    {
        var day = provider.GetDaySchedule(date);
        if (day.IsClosedAllDay)
        {
            return ErrorCodes.AvailabilityClosed;
        }

        if (!day.BaseIntervals.Any(r => r.Contains(start, duration)))
        {
            return ErrorCodes.AvailabilityOutsideWorkingHours;
        }

        if (!day.OpenIntervals.Any(r => r.Contains(start, duration)))
        {
            return ErrorCodes.AvailabilityClosed;
        }

        return ErrorCodes.AvailabilityMisalignedStart;
    }

    private static string RejectionMessage(DateTimeOffset requestedStart, TimeZoneInfo timeZone) =>
        $"O horário {Format(requestedStart, timeZone)} não é um início válido na agenda do prestador "
        + "(fora do expediente, em período fechado ou fora da grade de horários).";

    private static string Format(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
