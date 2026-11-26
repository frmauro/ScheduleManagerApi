using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.UnitTests;

/// <summary>Construtores de dados de teste. "Agora" padrão: segunda 2026-11-09 12:00Z (09:00 em São Paulo, UTC−3).</summary>
internal static class TestData
{
    public const string SaoPaulo = "America/Sao_Paulo";
    public const string NewYork = "America/New_York";

    public static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly NextMonday = new(2026, 11, 16);
    public static readonly DateOnly NextTuesday = new(2026, 11, 17);
    public static readonly TimeSpan SaoPauloOffset = TimeSpan.FromHours(-3);

    public static Address Address() => global::Scheduling.Domain.Providers.Address.Create("Rua A", "100", null, "Centro", "São Paulo", "SP", "01001000");

    public static Provider Provider(string timeZoneId = SaoPaulo, BookingPolicy? policy = null)
    {
        var provider = global::Scheduling.Domain.Providers.Provider.Create(Guid.NewGuid(), "Clínica Teste", null, ServiceCategory.Health, "contato@clinica.com", "+5511999998888", Address(), timeZoneId);
        if (policy is not null)
        {
            provider.UpdateBookingPolicy(policy);
        }

        return provider;
    }

    public static BookingPolicy Policy(bool autoConfirm = false, int step = 15, int buffer = 0, int notice = 60, int horizon = 60, int cancelNotice = 120) =>
        BookingPolicy.Create(autoConfirm, step, buffer, notice, horizon, cancelNotice);

    public static TimeRange Range(int startHour, int startMinute, int endHour, int endMinute) =>
        new(new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

    public static Provider WithHours(this Provider provider, DayOfWeek day, params TimeRange[] ranges)
    {
        var existing = provider.WorkingHours.Select(w => (w.DayOfWeek, w.Range)).Where(w => w.DayOfWeek != day);
        provider.ReplaceWorkingHours(existing.Concat(ranges.Select(r => (day, r))));
        return provider;
    }

    public static Service Service(Provider provider, int duration = 30, decimal price = 150m) =>
        global::Scheduling.Domain.Services.Service.Create(provider.Id, "Consulta", null, duration, price);

    /// <summary>Instante a partir de data/hora local de São Paulo (UTC−3, sem horário de verão).</summary>
    public static DateTimeOffset SaoPauloTime(DateOnly date, int hour, int minute = 0) =>
        new(date.ToDateTime(new TimeOnly(hour, minute)), SaoPauloOffset);

    public static Occupation Busy(DateOnly date, int hour, int minute, int durationMinutes, int bufferMinutes = 0)
    {
        var start = SaoPauloTime(date, hour, minute);
        return new Occupation(start, start.AddMinutes(durationMinutes + bufferMinutes));
    }

    public static Actor CustomerActor() => new(Guid.NewGuid(), Role.Customer);

    public static Actor ProviderActor() => new(Guid.NewGuid(), Role.Provider);

    public static Actor AdminActor() => new(Guid.NewGuid(), Role.Admin);

    public static Appointment Appointment(DateTimeOffset start, int duration = 30, bool autoConfirm = false, int buffer = 0)
    {
        var slot = new TimeSlot(start, start.AddMinutes(duration));
        return global::Scheduling.Domain.Appointments.Appointment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ServiceSnapshot("Consulta", duration, 150m),
            slot,
            buffer,
            autoConfirm,
            "Primeira consulta",
            null,
            CustomerActor(),
            Now);
    }
}
