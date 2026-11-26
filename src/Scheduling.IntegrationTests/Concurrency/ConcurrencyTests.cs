using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Persistence;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Concurrency;

/// <summary>SC-001 / FR-017 / FR-018: garantias de não-sobreposição sob requisições simultâneas.</summary>
public sealed class BookingConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<List<HttpResponseMessage>> FireAllAsync(IEnumerable<Func<Task<HttpResponseMessage>>> requests)
    {
        using var barrier = new SemaphoreSlim(0);
        var tasks = requests.Select(async send =>
        {
            await barrier.WaitAsync();
            return await send();
        }).ToList();

        barrier.Release(tasks.Count);
        return [.. await Task.WhenAll(tasks)];
    }

    private async Task<int> CountActiveAsync(Guid providerId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.Appointments.CountAsync(a => a.ProviderId == providerId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed));
    }

    [Fact]
    public async Task FiftyCustomersSameSlot_ExactlyOneSucceeds()
    {
        var provider = await Api.SetupProviderAsync();
        var customers = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Api.RegisterCustomerAsync()));

        var responses = await FireAllAsync(customers.Select(c => (Func<Task<HttpResponseMessage>>)(() =>
            TestApi.BookAsync(c.Client, provider, "2026-11-16T10:00:00-03:00"))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(49, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            await ProblemAssert.IsProblemAsync(conflict, HttpStatusCode.Conflict, ErrorCodes.AppointmentSlotUnavailable);
        }

        Assert.Equal(1, await CountActiveAsync(provider.ProviderId));
    }

    [Fact]
    public async Task OverlappingDifferentDurations_ExactlyOneAccepted()
    {
        // Repetido para aumentar a chance de intercalação real entre as duas transações.
        for (var round = 0; round < 5; round++)
        {
            if (round > 0)
            {
                await Fixture.ResetAsync();
            }

            var provider = await Api.SetupProviderAsync(durationMinutes: 45);
            var shortService = await TestApi.ReadAsync<Scheduling.Application.Services.ServiceDto>(
                await provider.Client.PostAsync("/api/v1/providers/me/services",
                    System.Net.Http.Json.JsonContent.Create(new Scheduling.Application.Services.ServiceRequest("Rápido", null, 30, 50m), options: TestApi.Json)),
                HttpStatusCode.Created);
            var c1 = await Api.RegisterCustomerAsync();
            var c2 = await Api.RegisterCustomerAsync();

            var responses = await FireAllAsync(
            [
                () => TestApi.BookAsync(c1.Client, provider, "2026-11-16T10:00:00-03:00"),                                   // 10:00–10:45
                () => TestApi.BookAsync(c2.Client, provider.ProviderId, shortService.Id, "2026-11-16T10:30:00-03:00"),     // 10:30–11:00
            ]);

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountActiveAsync(provider.ProviderId));
        }
    }

    [Fact]
    public async Task NonConflictingSlotsInParallel_AllSucceed()
    {
        var provider = await Api.SetupProviderAsync();
        var customers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Api.RegisterCustomerAsync()));
        string[] starts = ["09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "13:00", "13:30", "14:00", "14:30"];

        var responses = await FireAllAsync(customers.Select((c, i) => (Func<Task<HttpResponseMessage>>)(() =>
            TestApi.BookAsync(c.Client, provider, $"2026-11-16T{starts[i]}:00-03:00"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(10, await CountActiveAsync(provider.ProviderId));
    }

    [Fact]
    public async Task SameCustomerDoubleSubmitWithoutKey_OneCreatedOneConflict()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();

        var responses = await FireAllAsync(
        [
            () => TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00"),
            () => TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00"),
        ]);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await CountActiveAsync(provider.ProviderId));
    }
}

/// <summary>FR-022: alterações concorrentes de status.</summary>
public sealed class StatusConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TwoConcurrentWritersOnSameVersion_SecondGetsConcurrencyConflict()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        await using var scope1 = Fixture.Factory.Services.CreateAsyncScope();
        await using var scope2 = Fixture.Factory.Services.CreateAsyncScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<SchedulingDbContext>();

        // Ambos leem a mesma versão (intercalação determinística do cenário "prestador confirma enquanto cliente cancela").
        var forProvider = await db1.Appointments.Include(a => a.History).SingleAsync(a => a.Id == booked.Id);
        var forCustomer = await db2.Appointments.Include(a => a.History).SingleAsync(a => a.Id == booked.Id);

        forProvider.Confirm(new Actor(provider.Auth.User.Id, Role.Provider), Clock.Now);
        forCustomer.Cancel(new Actor(customer.Auth.User.Id, Role.Customer), null, "Desisti", 120, Clock.Now);

        await db1.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => db2.SaveChangesAsync());
        Assert.Equal(ErrorCodes.AppointmentConcurrencyConflict, ex.Code);

        var final = await TestApi.ReadAsync<AppointmentDto>(await customer.Client.GetAsync($"/api/v1/appointments/{booked.Id}"), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.Confirmed, final.Status);
    }

    [Fact]
    public async Task ParallelCancelAndRebook_NeverTwoActiveOverlapping()
    {
        var provider = await Api.SetupProviderAsync();
        var first = await Api.RegisterCustomerAsync();
        var second = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(first.Client, provider, "2026-11-16T10:00:00-03:00");

        var cancel = TestApi.PostActionAsync(first.Client, booked.Id, "cancel", new CancelAppointmentRequest("Desisti", null));
        var rebook = TestApi.BookAsync(second.Client, provider, "2026-11-16T10:00:00-03:00");
        await Task.WhenAll(cancel, rebook);

        Assert.Equal(HttpStatusCode.OK, cancel.Result.StatusCode);
        Assert.Contains(rebook.Result.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var active = await db.Appointments.CountAsync(a => a.ProviderId == provider.ProviderId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed));
        Assert.True(active <= 1);
    }
}
