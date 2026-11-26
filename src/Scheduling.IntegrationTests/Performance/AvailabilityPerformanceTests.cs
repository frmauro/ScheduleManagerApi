using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Persistence;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Performance;

/// <summary>SC-002: 95% das consultas de disponibilidade em até 1 s com 500 agendamentos futuros.</summary>
[Trait("Category", "Performance")]
public sealed class AvailabilityPerformanceTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Availability_P95_Under1Second_With500FutureAppointments()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var bookedDays = await SeedAppointmentsAsync(provider, customer, 500);

        var anon = Api.Anonymous();
        var durations = new List<double>();
        for (var i = 0; i < 200; i++)
        {
            var day = bookedDays[i % bookedDays.Count];
            var watch = Stopwatch.StartNew();
            var response = await anon.GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date={day:yyyy-MM-dd}");
            watch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            durations.Add(watch.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        var p95 = durations[(int)Math.Ceiling(durations.Count * 0.95) - 1];
        Assert.True(p95 < 1000, $"p95 = {p95:0.0} ms (meta < 1000 ms)");
    }

    /// <summary>Insere agendamentos válidos (dias úteis, 09–12 e 13–18, 30 min) diretamente pelo domínio.</summary>
    private async Task<List<DateOnly>> SeedAppointmentsAsync(ProviderSetup provider, CustomerSetup customer, int count)
    {
        var offset = TimeSpan.FromHours(-3);
        var actor = new Actor(customer.Auth.User.Id, Role.Customer);
        var snapshot = new ServiceSnapshot("Consulta", 30, 150m);
        var appointments = new List<Appointment>();
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2026, 11, 10); appointments.Count < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            days.Add(day);
            foreach (var hour in new[] { 9, 10, 11, 13, 14, 15, 16, 17 })
            {
                foreach (var minute in new[] { 0, 30 })
                {
                    if (appointments.Count == count)
                    {
                        break;
                    }

                    var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), offset);
                    appointments.Add(Appointment.Create(provider.ProviderId, customer.CustomerId, provider.ServiceId, snapshot,
                        new TimeSlot(start, start.AddMinutes(30)), 0, false, null, null, actor, Clock.Now));
                }
            }
        }

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        db.Appointments.AddRange(appointments);
        await db.SaveChangesAsync();
        return days;
    }
}
