using System.Net;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

/// <summary>US3 — ciclo de vida do agendamento.</summary>
public sealed class AppointmentLifecycleTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string TenAm = "2026-11-16T10:00:00-03:00";

    [Fact]
    public async Task ProviderConfirmsPending_ETagChanges()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);

        var response = await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm");
        var confirmed = await TestApi.ReadAsync<AppointmentDto>(response, HttpStatusCode.OK);

        Assert.Equal(AppointmentStatus.Confirmed, confirmed.Status);
        Assert.NotEqual(booked.Version, confirmed.Version);
        Assert.Equal($"W/\"{confirmed.Version}\"", response.Headers.ETag?.ToString());
        Assert.Equal(2, confirmed.History.Count);
    }

    [Fact]
    public async Task CustomerCancelsWithinWindow_SlotReappears()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.OK);

        var cancelled = await TestApi.ReadAsync<AppointmentDto>(
            await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("Imprevisto", null)), HttpStatusCode.OK);

        Assert.Equal(AppointmentStatus.CancelledByCustomer, cancelled.Status);
        Assert.Equal("Imprevisto", cancelled.CancellationReason);
        Assert.Contains("10:00", await TestApi.SlotStartsAsync(Api.Anonymous(), provider, TestApi.NextMonday));
    }

    [Fact]
    public async Task CustomerCancelInsideNoticeWindow_Returns422()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        Clock.Now = new DateTimeOffset(2026, 11, 16, 11, 30, 0, TimeSpan.Zero); // 08:30 local, 1h30 antes
        var customerNow = await Api.LoginAsync(customer.Auth.User.Email);
        var providerNow = await Api.LoginAsync(provider.Auth.User.Email);

        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customerNow, booked.Id, "cancel", new CancelAppointmentRequest("Imprevisto", null)),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AppointmentCancellationWindow);

        // O prestador ainda pode cancelar.
        var byProvider = await TestApi.ReadAsync<AppointmentDto>(
            await TestApi.PostActionAsync(providerNow, booked.Id, "cancel", new CancelAppointmentRequest("Imprevisto na agenda", null)), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.CancelledByProvider, byProvider.Status);
    }

    [Fact]
    public async Task CompleteBeforeStart_Returns422_AfterStart_Returns200()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.OK);

        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "complete"),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AppointmentNotStarted);

        Clock.Now = new DateTimeOffset(2026, 11, 16, 13, 31, 0, TimeSpan.Zero);
        var provider2 = await Api.LoginAsync(provider.Auth.User.Email);
        var completed = await TestApi.ReadAsync<AppointmentDto>(await TestApi.PostActionAsync(provider2, booked.Id, "complete"), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.Completed, completed.Status);

        // Estado final: nenhuma transição.
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(provider2, booked.Id, "confirm"),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AppointmentInvalidTransition);
    }

    [Fact]
    public async Task CancelledIsFinal_Returns422()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "cancel", new CancelAppointmentRequest("Agenda", null)), HttpStatusCode.OK);

        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm"), HttpStatusCode.UnprocessableEntity, ErrorCodes.AppointmentInvalidTransition);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("De novo", null)),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AppointmentInvalidTransition);
    }

    [Fact]
    public async Task AuthorizationRules()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var stranger = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);

        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "confirm"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "complete"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(stranger.Client, booked.Id, "cancel", new CancelAppointmentRequest("Hack", null)),
            HttpStatusCode.NotFound, ErrorCodes.AppointmentNotFound);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("Motivo", CancellationParty.Provider)),
            HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("x", null)),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task AdminCancelsOnBehalfOfCustomer()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        var admin = await Api.AdminAsync();

        var cancelled = await TestApi.ReadAsync<AppointmentDto>(
            await TestApi.PostActionAsync(admin, booked.Id, "cancel", new CancelAppointmentRequest("Ajuste", CancellationParty.Customer)), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.CancelledByCustomer, cancelled.Status);
        Assert.Equal(Scheduling.Domain.Users.Role.Admin, cancelled.History.Last().ChangedByRole);
    }

    [Fact]
    public async Task StaleIfMatch_Returns412_CurrentIfMatch_Succeeds()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, TenAm);
        var staleETag = $"W/\"{booked.Version}\"";

        var confirmed = await TestApi.ReadAsync<AppointmentDto>(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm", ifMatch: staleETag), HttpStatusCode.OK);

        await ProblemAssert.IsProblemAsync(
            await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("Desisti", null), staleETag),
            HttpStatusCode.PreconditionFailed, ErrorCodes.AppointmentPreconditionFailed);

        await TestApi.EnsureAsync(
            await TestApi.PostActionAsync(customer.Client, booked.Id, "cancel", new CancelAppointmentRequest("Desisti", null), $"W/\"{confirmed.Version}\""),
            HttpStatusCode.OK);
    }
}

/// <summary>FR-024 — agenda do prestador.</summary>
public sealed class ProviderAgendaTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Agenda_PagingFiltersAndOwnership()
    {
        var provider = await Api.SetupProviderAsync();
        var other = await Api.SetupProviderAsync(displayName: "Outro");
        var c1 = await Api.RegisterCustomerAsync();
        var c2 = await Api.RegisterCustomerAsync();

        var a1 = await Api.BookOkAsync(c1.Client, provider, "2026-11-16T09:00:00-03:00");
        await Api.BookOkAsync(c2.Client, provider, "2026-11-17T09:00:00-03:00");
        await Api.BookOkAsync(c1.Client, provider, "2026-11-18T09:00:00-03:00");
        await Api.BookOkAsync(c2.Client, other, "2026-11-16T09:00:00-03:00");
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(provider.Client, a1.Id, "confirm"), HttpStatusCode.OK);

        var all = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(await provider.Client.GetAsync("/api/v1/providers/me/appointments"), HttpStatusCode.OK);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(all.Items.OrderBy(i => i.StartAt).Select(i => i.Id), all.Items.Select(i => i.Id));
        Assert.All(all.Items, i => Assert.Equal(provider.ProviderId, i.ProviderId));

        var confirmed = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await provider.Client.GetAsync("/api/v1/providers/me/appointments?status=Confirmed"), HttpStatusCode.OK);
        Assert.Equal(a1.Id, Assert.Single(confirmed.Items).Id);

        var multi = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await provider.Client.GetAsync("/api/v1/providers/me/appointments?status=Confirmed&status=Pending&sort=-startAt&pageSize=2"), HttpStatusCode.OK);
        Assert.Equal(3, multi.TotalCount);
        Assert.Equal(2, multi.Items.Count);
        Assert.True(multi.Items[0].StartAt > multi.Items[1].StartAt);

        var period = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await provider.Client.GetAsync("/api/v1/providers/me/appointments?from=2026-11-17T00:00:00-03:00&to=2026-11-18T00:00:00-03:00"), HttpStatusCode.OK);
        Assert.Single(period.Items);

        var byService = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await provider.Client.GetAsync($"/api/v1/providers/me/appointments?serviceId={Guid.NewGuid()}"), HttpStatusCode.OK);
        Assert.Empty(byService.Items);

        await ProblemAssert.IsProblemAsync(await provider.Client.GetAsync("/api/v1/providers/me/appointments?status=Unknown"), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }
}
