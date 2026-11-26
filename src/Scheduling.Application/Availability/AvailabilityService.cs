using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Providers;
using Scheduling.Application.Services;
using Scheduling.Domain.Availability;

namespace Scheduling.Application.Availability;

public sealed class AvailabilityQuery
{
    public Guid ServiceId { get; set; }

    public DateOnly? Date { get; set; }
}

public sealed record TimeSlotDto(DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record AvailabilityResponse(
    Guid ProviderId,
    Guid ServiceId,
    DateOnly Date,
    string TimeZoneId,
    int DurationMinutes,
    IReadOnlyList<TimeSlotDto> Slots);

public sealed class AvailabilityQueryValidator : AbstractValidator<AvailabilityQuery>
{
    public AvailabilityQueryValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Date).NotNull().WithMessage("'date' é obrigatória no formato yyyy-MM-dd.");
    }
}

/// <summary>Consulta de horários livres (FR-014). Não reserva: a criação revalida (FR-016).</summary>
public sealed class AvailabilityService(
    IProviderRepository providers,
    IServiceRepository services,
    IAppointmentRepository appointments,
    TimeProvider timeProvider)
{
    public async Task<AvailabilityResponse> GetAsync(Guid providerId, AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        var date = query.Date!.Value;
        var provider = await providers.GetWithScheduleAsync(providerId, date, date, cancellationToken);
        if (provider is null || !provider.IsActive)
        {
            throw ProviderService.NotFound();
        }

        var service = await services.GetByIdAsync(query.ServiceId, cancellationToken);
        if (service is null || !service.IsActive || service.ProviderId != provider.Id)
        {
            throw ServiceCatalogService.NotFound();
        }

        var occupations = await appointments.GetOccupationsAsync(provider.Id, DayStartUtc(date.AddDays(-1)), DayStartUtc(date.AddDays(2)), cancellationToken);
        var slots = AvailabilityCalculator.GetAvailableSlots(provider, service, date, occupations, timeProvider.GetUtcNow());

        return new AvailabilityResponse(
            provider.Id,
            service.Id,
            date,
            provider.TimeZoneId,
            service.DurationMinutes,
            slots.Select(s => new TimeSlotDto(ProviderTime.Convert(s.StartAt, provider.TimeZoneId), ProviderTime.Convert(s.EndAt, provider.TimeZoneId))).ToList());
    }

    /// <summary>Janela UTC folgada que cobre o dia local em qualquer fuso (−14h a +14h).</summary>
    internal static DateTimeOffset DayStartUtc(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
