using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Notifications;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Notifications;
using Scheduling.Infrastructure.Persistence;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Notifications;

/// <summary>US5 — outbox de notificações (FR-025, FR-027).</summary>
public sealed class NotificationOutboxTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<int> RunDispatcherAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().RunOnceAsync();
    }

    [Fact]
    public async Task Lifecycle_GeneratesNotificationsForEachParty()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.OK);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("Imprevisto", null)), HttpStatusCode.OK);

        var forCustomer = await TestApi.ReadAsync<PagedResult<NotificationDto>>(await customer.Client.GetAsync("/api/v1/notifications/me"), HttpStatusCode.OK);
        Assert.Equal(
            [NotificationType.AppointmentCreated, NotificationType.AppointmentConfirmed],
            forCustomer.Items.Select(n => n.Type).OrderBy(t => t));

        var forProvider = await TestApi.ReadAsync<PagedResult<NotificationDto>>(await provider.Client.GetAsync("/api/v1/notifications/me"), HttpStatusCode.OK);
        Assert.Equal(
            [NotificationType.AppointmentCreated, NotificationType.AppointmentCancelled],
            forProvider.Items.Select(n => n.Type).OrderBy(t => t));
        Assert.Contains("Imprevisto", forProvider.Items.Single(n => n.Type == NotificationType.AppointmentCancelled).Body);

        var onlyCancelled = await TestApi.ReadAsync<PagedResult<NotificationDto>>(await provider.Client.GetAsync("/api/v1/notifications/me?type=AppointmentCancelled"), HttpStatusCode.OK);
        Assert.Single(onlyCancelled.Items);
    }

    [Fact]
    public async Task Dispatcher_MarksPendingAsSent_AdminCanAudit()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        Assert.Equal(2, await RunDispatcherAsync(Fixture.Factory.Services));
        Assert.Equal(0, await RunDispatcherAsync(Fixture.Factory.Services));

        var admin = await Api.AdminAsync();
        var sent = await TestApi.ReadAsync<PagedResult<NotificationDto>>(
            await admin.GetAsync($"/api/v1/admin/notifications?status=Sent&appointmentId={booked.Id}"), HttpStatusCode.OK);
        Assert.Equal(2, sent.TotalCount);
        Assert.All(sent.Items, n => Assert.Equal(Clock.Now, n.SentAt));
    }

    [Fact]
    public async Task ChannelFailure_RetriesThenFails_WithoutAffectingAppointment()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        using var failing = Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationChannel>();
            services.AddSingleton<INotificationChannel, FailingChannel>();
        }));

        Assert.Equal(2, await RunDispatcherAsync(failing.Services));   // tentativa 1
        Assert.Equal(0, await RunDispatcherAsync(failing.Services));   // aguardando backoff de 1 min
        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, await RunDispatcherAsync(failing.Services));   // tentativa 2
        Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, await RunDispatcherAsync(failing.Services));   // tentativa 3 → Failed
        Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await RunDispatcherAsync(failing.Services));

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var stored = await db.Notifications.AsNoTracking().Where(n => n.AppointmentId == booked.Id).ToListAsync();
        Assert.All(stored, n =>
        {
            Assert.Equal(NotificationStatus.Failed, n.Status);
            Assert.Equal(3, n.Attempts);
            Assert.Contains("canal indisponível", n.LastError);
        });

        var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
    }

    private sealed class FailingChannel : INotificationChannel
    {
        public Task SendAsync(Notification notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("canal indisponível (simulado)");
    }
}

/// <summary>US5 — lembretes 24h antes (FR-026, SC-005).</summary>
public sealed class ReminderTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    // Agora = 2026-11-09 12:00Z. Início 2026-11-10 10:00 local = 13:00Z → 25h de antecedência.
    private const string In25Hours = "2026-11-10T10:00:00-03:00";

    private async Task<int> RunRemindersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReminderProcessor>().RunOnceAsync();
    }

    private async Task<List<Notification>> RemindersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.Notifications.AsNoTracking().Where(n => n.Type == NotificationType.AppointmentReminder).ToListAsync();
    }

    [Fact]
    public async Task ConfirmedAppointment_GetsExactlyOneReminderAt24h()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, In25Hours);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.OK);

        Assert.Equal(0, await RunRemindersAsync()); // ainda faltam 25h

        Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await RunRemindersAsync());
        Assert.Equal(0, await RunRemindersAsync());
        Assert.Equal(0, await RunRemindersAsync());

        var reminder = Assert.Single(await RemindersAsync());
        Assert.Equal(customer.Auth.User.Id, reminder.RecipientUserId);
        Assert.Equal(booked.Id, reminder.AppointmentId);
    }

    [Fact]
    public async Task PendingOrCancelled_GetNoReminder()
    {
        var provider = await Api.SetupProviderAsync();
        var c1 = await Api.RegisterCustomerAsync();
        var c2 = await Api.RegisterCustomerAsync();
        await Api.BookOkAsync(c1.Client, provider, In25Hours); // permanece Pending
        var cancelled = await Api.BookOkAsync(c2.Client, provider, "2026-11-10T11:00:00-03:00");
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, cancelled.Id, "confirm"), HttpStatusCode.OK);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(c2.Client, cancelled.Id, "cancel", new CancelAppointmentRequest("Desisti", null)), HttpStatusCode.OK);

        Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(0, await RunRemindersAsync());
        Assert.Empty(await RemindersAsync());
    }

    [Fact]
    public async Task BookedWithLessThan24hNotice_GetsNoReminder()
    {
        var provider = await Api.SetupProviderAsync(policy: new Scheduling.Application.Providers.BookingPolicyDto(true, 15, 0, 60, 60, 120));
        var customer = await Api.RegisterCustomerAsync();
        await Api.BookOkAsync(customer.Client, provider, "2026-11-09T14:00:00-03:00"); // auto-confirmado, 5h de antecedência

        Assert.Equal(0, await RunRemindersAsync());
        Assert.Empty(await RemindersAsync());
    }

    [Fact]
    public async Task ReminderOfAppointmentCancelledLater_IsCancelledByDispatcher()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, In25Hours);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.OK);

        Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await RunRemindersAsync());

        var providerNow = await Api.LoginAsync(provider.Auth.User.Email);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(providerNow, booked.Id, "cancel", new CancelAppointmentRequest("Agenda", null)), HttpStatusCode.OK);

        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().RunOnceAsync();
        }

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await RemindersAsync()).Status);
    }
}
