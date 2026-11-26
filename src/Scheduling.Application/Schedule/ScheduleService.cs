using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Common.Validation;
using Scheduling.Application.Providers;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;

namespace Scheduling.Application.Schedule;

/// <summary>Expediente semanal e exceções (FR-003, FR-004, FR-007).</summary>
public sealed class ScheduleService(
    IProviderRepository providers,
    IAppointmentRepository appointments,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<WeeklyScheduleDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var provider = await LoadAsync(DateOnly.MinValue, DateOnly.MinValue, cancellationToken);
        return WeeklyScheduleDto.From(provider);
    }

    public async Task<ScheduleChangeResultDto> ReplaceWorkingHoursAsync(WeeklyScheduleDto request, CancellationToken cancellationToken = default)
    {
        var (from, to) = AffectedWindow();
        var provider = await LoadAsync(from, to, cancellationToken);

        provider.ReplaceWorkingHours(request.Days.SelectMany(day => day.Intervals.Select(i =>
            (day.DayOfWeek, new TimeRange(RuleExtensions.ParseLocalTime(i.Start), RuleExtensions.ParseLocalTime(i.End))))));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ScheduleChangeResultDto(WeeklyScheduleDto.From(provider), await FindAffectedAsync(provider, cancellationToken));
    }

    public async Task<PagedResult<ScheduleExceptionDto>> ListExceptionsAsync(ScheduleExceptionListQuery query, CancellationToken cancellationToken = default)
    {
        var result = await providers.ListExceptionsAsync(currentUser.RequireProviderId(), query.From, query.To, query, cancellationToken);
        return result.Map(ScheduleExceptionDto.From);
    }

    public async Task<ScheduleExceptionCreatedDto> CreateExceptionAsync(ScheduleExceptionRequest request, CancellationToken cancellationToken = default)
    {
        var (from, to) = AffectedWindow();
        var provider = await LoadAsync(Min(from, request.StartDate), Max(to, request.EndDate), cancellationToken);

        var exception = provider.AddException(
            request.Type,
            request.StartDate,
            request.EndDate,
            request.StartTime is null ? null : RuleExtensions.ParseLocalTime(request.StartTime),
            request.EndTime is null ? null : RuleExtensions.ParseLocalTime(request.EndTime),
            request.Reason,
            provider.GetLocalToday(timeProvider.GetUtcNow()));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ScheduleExceptionCreatedDto(ScheduleExceptionDto.From(exception), await FindAffectedAsync(provider, cancellationToken));
    }

    public async Task DeleteExceptionAsync(Guid exceptionId, CancellationToken cancellationToken = default)
    {
        var provider = await LoadAsync(DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);
        provider.RemoveException(exceptionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Janela que cobre qualquer agendamento futuro possível (horizonte máximo de 365 dias).</summary>
    private (DateOnly From, DateOnly To) AffectedWindow()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);
        return (today, today.AddDays(ValidationLimits.HorizonDaysMax + 2));
    }

    /// <summary>FR-007: agendamentos ativos futuros que deixaram de caber na agenda — informados, nunca cancelados.</summary>
    private async Task<IReadOnlyList<AppointmentSummaryDto>> FindAffectedAsync(Provider provider, CancellationToken cancellationToken)
    {
        var active = await appointments.GetActiveFromAsync(provider.Id, timeProvider.GetUtcNow(), cancellationToken);
        var affectedIds = active.Where(a => !provider.IsWithinSchedule(a.StartAt, a.EndAt)).Select(a => a.Id).ToList();
        if (affectedIds.Count == 0)
        {
            return [];
        }

        var items = await appointments.GetListItemsAsync(affectedIds, cancellationToken);
        return items.OrderBy(i => i.StartAt).Select(AppointmentSummaryDto.From).ToList();
    }

    private async Task<Provider> LoadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await providers.GetWithScheduleAsync(currentUser.RequireProviderId(), from, to, cancellationToken) ?? throw ProviderService.NotFound();

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
