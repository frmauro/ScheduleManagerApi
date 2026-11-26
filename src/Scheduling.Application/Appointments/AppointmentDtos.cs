using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Appointments;

/// <summary>Criação de agendamento. <c>StartAt</c> é texto ISO 8601 e DEVE conter offset (FR-031).</summary>
public sealed record CreateAppointmentRequest(Guid ProviderId, Guid ServiceId, string StartAt, string? Notes)
{
    public DateTimeOffset ParseStartAt() => DateTimeOffset.Parse(StartAt, CultureInfo.InvariantCulture, DateTimeStyles.None);
}

public sealed record CancelAppointmentRequest(string Reason, CancellationParty? OnBehalfOf);

public sealed record ProviderRefDto(Guid Id, string DisplayName, string TimeZoneId);

public sealed record CustomerRefDto(Guid Id, string Name, string Email, string Phone);

public sealed record ServiceSnapshotDto(Guid Id, string Name, int DurationMinutes, decimal Price);

public sealed record StatusChangeDto(AppointmentStatus? FromStatus, AppointmentStatus ToStatus, Role ChangedByRole, string? Reason, DateTimeOffset ChangedAt);

public sealed record AppointmentDto(
    Guid Id,
    ProviderRefDto Provider,
    CustomerRefDto Customer,
    ServiceSnapshotDto Service,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    AppointmentStatus Status,
    string? Notes,
    string? CancellationReason,
    string Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StatusChangeDto> History)
{
    public static AppointmentDto From(Appointment a, Provider provider, Customer customer)
    {
        var tz = provider.TimeZoneId;
        return new AppointmentDto(
            a.Id,
            new ProviderRefDto(provider.Id, provider.DisplayName, tz),
            new CustomerRefDto(customer.Id, customer.Name, customer.Email, customer.Phone),
            new ServiceSnapshotDto(a.ServiceId, a.Service.Name, a.Service.DurationMinutes, a.Service.Price),
            ProviderTime.Convert(a.StartAt, tz),
            ProviderTime.Convert(a.EndAt, tz),
            a.Status,
            a.CustomerNotes,
            a.CancellationReason,
            a.Version.ToString(CultureInfo.InvariantCulture),
            ProviderTime.Convert(a.CreatedAt, tz),
            a.History
                .OrderBy(h => h.ChangedAt)
                .Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedByRole, h.Reason, ProviderTime.Convert(h.ChangedAt, tz)))
                .ToList());
    }
}

public sealed record AppointmentSummaryDto(
    Guid Id,
    Guid ProviderId,
    string ProviderName,
    Guid CustomerId,
    string CustomerName,
    string ServiceName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    AppointmentStatus Status)
{
    public static AppointmentSummaryDto From(AppointmentListItem i) =>
        new(
            i.Id,
            i.ProviderId,
            i.ProviderName,
            i.CustomerId,
            i.CustomerName,
            i.ServiceName,
            ProviderTime.Convert(i.StartAt, i.ProviderTimeZoneId),
            ProviderTime.Convert(i.EndAt, i.ProviderTimeZoneId),
            i.Status);
}

/// <summary>Converte instantes UTC para o fuso do prestador (respostas sempre com offset explícito).</summary>
public static class ProviderTime
{
    public static DateTimeOffset Convert(DateTimeOffset instant, string timeZoneId) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
}

/// <summary>Filtros comuns de listagens de agendamentos (agenda do prestador e histórico do cliente).</summary>
public class AppointmentListQuery : PageQuery
{
    public const string SortAscending = "startAt";
    public const string SortDescending = "-startAt";

    public List<AppointmentStatus> Status { get; set; } = [];

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public string? Sort { get; set; }
}

public sealed class AgendaQuery : AppointmentListQuery
{
    public Guid? ServiceId { get; set; }
}

public abstract class AppointmentListQueryValidator<T> : PageQueryValidator<T>
    where T : AppointmentListQuery
{
    protected AppointmentListQueryValidator()
    {
        RuleForEach(x => x.Status).IsInEnum();
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'to' deve ser posterior a 'from'.");
        RuleFor(x => x.To)
            .Must((x, to) => (to!.Value - x.From!.Value).TotalDays <= ValidationLimits.MaxPeriodDays)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage($"O período deve ter no máximo {ValidationLimits.MaxPeriodDays} dias.");
        RuleFor(x => x.Sort)
            .Must(s => s is null or AppointmentListQuery.SortAscending or AppointmentListQuery.SortDescending)
            .WithMessage("'sort' deve ser 'startAt' ou '-startAt'.");
    }
}

public sealed class AgendaQueryValidator : AppointmentListQueryValidator<AgendaQuery>;

public sealed partial class CreateAppointmentRequestValidator : AbstractValidator<CreateAppointmentRequest>
{
    public CreateAppointmentRequestValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.StartAt)
            .NotEmpty()
            .Must(HasExplicitOffset).WithMessage("'startAt' deve ser ISO 8601 com deslocamento de fuso (ex.: 2026-11-16T10:00:00-03:00).");
        RuleFor(x => x.Notes).MaximumLength(ValidationLimits.NotesMaxLength);
    }

    public static bool HasExplicitOffset(string? value) =>
        value is not null
        && OffsetRegex().IsMatch(value)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetRegex();
}

public sealed class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
{
    public CancelAppointmentRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().Length(ValidationLimits.CancellationReasonMinLength, ValidationLimits.CancellationReasonMaxLength);
        RuleFor(x => x.OnBehalfOf).IsInEnum();
    }
}
