using System.Net;
using System.Net.Http.Json;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Providers;
using Scheduling.Application.Schedule;
using Scheduling.Application.Services;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

/// <summary>US2 — perfil, política e catálogo de prestadores.</summary>
public sealed class ProvidersEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAndUpdateOwnProfile()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        var update = new UpdateProviderRequest("Novo Nome", "Desc", ServiceCategory.Dental, "novo@ex.com", "+5521999990000",
            TestApi.Address with { City = "Rio de Janeiro", State = "RJ" }, "America/Manaus");

        var updated = await TestApi.ReadAsync<ProviderProfileDto>(await client.PutAsJsonAsync("/api/v1/providers/me", update, TestApi.Json), HttpStatusCode.OK);
        Assert.Equal("Novo Nome", updated.DisplayName);
        Assert.Equal("America/Manaus", updated.TimeZoneId);

        var reloaded = await TestApi.ReadAsync<ProviderProfileDto>(await client.GetAsync("/api/v1/providers/me"), HttpStatusCode.OK);
        Assert.Equal("Rio de Janeiro", reloaded.Address.City);
        Assert.Equal(ServiceCategory.Dental, reloaded.Category);
    }

    [Fact]
    public async Task UpdateBookingPolicy_ValidAndOutOfRange()
    {
        var (client, _) = await Api.RegisterProviderAsync();

        var ok = await TestApi.ReadAsync<BookingPolicyDto>(
            await client.PutAsJsonAsync("/api/v1/providers/me/booking-policy", new BookingPolicyDto(true, 30, 10, 0, 90, 60), TestApi.Json), HttpStatusCode.OK);
        Assert.True(ok.AutoConfirm);

        var bad = await client.PutAsJsonAsync("/api/v1/providers/me/booking-policy", new BookingPolicyDto(false, 7, 0, 60, 400, 120), TestApi.Json);
        var problem = await ProblemAssert.IsProblemAsync(bad, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.True(problem.GetProperty("errors").TryGetProperty("slotStepMinutes", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("bookingHorizonDays", out _));
    }

    [Fact]
    public async Task Catalog_FiltersPagingAndSort()
    {
        await Api.RegisterProviderAsync(displayName: "Barbearia Alpha", category: ServiceCategory.Barbershop);
        await Api.RegisterProviderAsync(displayName: "Barbearia Beta", category: ServiceCategory.Barbershop);
        await Api.RegisterProviderAsync(displayName: "Clínica Gama", category: ServiceCategory.Health);
        var anon = Api.Anonymous();

        var barbers = await TestApi.ReadAsync<PagedResult<ProviderSummaryDto>>(await anon.GetAsync("/api/v1/providers?category=Barbershop&sort=-displayName"), HttpStatusCode.OK);
        Assert.Equal(["Barbearia Beta", "Barbearia Alpha"], barbers.Items.Select(i => i.DisplayName));

        var page = await TestApi.ReadAsync<PagedResult<ProviderSummaryDto>>(await anon.GetAsync("/api/v1/providers?page=2&pageSize=2"), HttpStatusCode.OK);
        Assert.Equal(3, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalPages);

        var byText = await TestApi.ReadAsync<PagedResult<ProviderSummaryDto>>(await anon.GetAsync("/api/v1/providers?q=gama&city=s%C3%A3o%20paulo"), HttpStatusCode.OK);
        Assert.Equal("Clínica Gama", Assert.Single(byText.Items).DisplayName);

        await ProblemAssert.IsProblemAsync(await anon.GetAsync("/api/v1/providers?pageSize=101"), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await ProblemAssert.IsProblemAsync(await anon.GetAsync("/api/v1/providers?category=Unknown"), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task PublicProfile_UnknownProvider_Returns404()
    {
        var (_, auth) = await Api.RegisterProviderAsync();
        var anon = Api.Anonymous();

        var profile = await TestApi.ReadAsync<ProviderPublicProfileDto>(await anon.GetAsync($"/api/v1/providers/{auth.User.ProviderId}"), HttpStatusCode.OK);
        Assert.Equal("Clínica Teste", profile.DisplayName);
        await ProblemAssert.IsProblemAsync(await anon.GetAsync($"/api/v1/providers/{Guid.NewGuid()}"), HttpStatusCode.NotFound, ErrorCodes.ProviderNotFound);
    }

    [Fact]
    public async Task CustomerCannotUpdateProviderProfile_Returns403()
    {
        var customer = await Api.RegisterCustomerAsync();
        var update = new UpdateProviderRequest("X", null, ServiceCategory.Other, "x@x.com", "+5511999998888", TestApi.Address, "America/Sao_Paulo");
        await ProblemAssert.IsProblemAsync(await customer.Client.PutAsJsonAsync("/api/v1/providers/me", update, TestApi.Json), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
    }
}

/// <summary>US2 — serviços.</summary>
public sealed class ServicesEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_Returns201WithLocation_AndAppearsInPublicList()
    {
        var (client, auth) = await Api.RegisterProviderAsync();
        var response = await client.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("Corte", "Corte masculino", 45, 60m), TestApi.Json);
        var service = await TestApi.ReadAsync<ServiceDto>(response, HttpStatusCode.Created);

        Assert.Equal($"/api/v1/providers/me/services/{service.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(45, service.DurationMinutes);
        Assert.Equal(60m, service.Price);

        var list = await TestApi.ReadAsync<PagedResult<ServiceDto>>(await Api.Anonymous().GetAsync($"/api/v1/providers/{auth.User.ProviderId}/services"), HttpStatusCode.OK);
        Assert.Equal(service.Id, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task DuplicateNameIgnoringCase_Returns409()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        await TestApi.EnsureAsync(await client.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("Corte", null, 30, 50m), TestApi.Json), HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("CORTE", null, 30, 50m), TestApi.Json);
        await ProblemAssert.IsProblemAsync(duplicate, HttpStatusCode.Conflict, ErrorCodes.ServiceNameTaken);
    }

    [Fact]
    public async Task SameNameForDifferentProviders_IsAllowed()
    {
        var (a, _) = await Api.RegisterProviderAsync();
        var (b, _) = await Api.RegisterProviderAsync();
        await TestApi.EnsureAsync(await a.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("Corte", null, 30, 50m), TestApi.Json), HttpStatusCode.Created);
        await TestApi.EnsureAsync(await b.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("Corte", null, 30, 50m), TestApi.Json), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_Deactivate_ListAndDelete()
    {
        var setup = await Api.SetupProviderAsync();
        var client = setup.Client;

        var updated = await TestApi.ReadAsync<ServiceDto>(
            await client.PutAsJsonAsync($"/api/v1/providers/me/services/{setup.ServiceId}", new ServiceRequest("Consulta Longa", null, 60, 200m), TestApi.Json), HttpStatusCode.OK);
        Assert.Equal(60, updated.DurationMinutes);

        var deactivated = await TestApi.ReadAsync<ServiceDto>(
            await client.PatchAsJsonAsync($"/api/v1/providers/me/services/{setup.ServiceId}/status", new ActiveStatusRequest(false), TestApi.Json), HttpStatusCode.OK);
        Assert.False(deactivated.IsActive);

        var publicList = await TestApi.ReadAsync<PagedResult<ServiceDto>>(await Api.Anonymous().GetAsync($"/api/v1/providers/{setup.ProviderId}/services"), HttpStatusCode.OK);
        Assert.Empty(publicList.Items);

        var inactive = await TestApi.ReadAsync<PagedResult<ServiceDto>>(await client.GetAsync("/api/v1/providers/me/services?isActive=false"), HttpStatusCode.OK);
        Assert.Single(inactive.Items);

        await TestApi.EnsureAsync(await client.DeleteAsync($"/api/v1/providers/me/services/{setup.ServiceId}"), HttpStatusCode.NoContent);
        await ProblemAssert.IsProblemAsync(await client.DeleteAsync($"/api/v1/providers/me/services/{setup.ServiceId}"), HttpStatusCode.NotFound, ErrorCodes.ServiceNotFound);
    }

    [Fact]
    public async Task ServiceOfAnotherProvider_Returns404()
    {
        var owner = await Api.SetupProviderAsync();
        var (other, _) = await Api.RegisterProviderAsync();

        var response = await other.PutAsJsonAsync($"/api/v1/providers/me/services/{owner.ServiceId}", new ServiceRequest("Hack", null, 30, 1m), TestApi.Json);
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.ServiceNotFound);
    }

    [Fact]
    public async Task DeleteBookedService_Returns409_AndDeactivationKeepsAppointment()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var appointment = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        await ProblemAssert.IsProblemAsync(await provider.Client.DeleteAsync($"/api/v1/providers/me/services/{provider.ServiceId}"), HttpStatusCode.Conflict, ErrorCodes.ServiceInUse);

        await TestApi.EnsureAsync(await provider.Client.PatchAsJsonAsync($"/api/v1/providers/me/services/{provider.ServiceId}/status", new ActiveStatusRequest(false), TestApi.Json), HttpStatusCode.OK);
        var reloaded = await TestApi.ReadAsync<AppointmentDto>(await customer.Client.GetAsync($"/api/v1/appointments/{appointment.Id}"), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.Pending, reloaded.Status);
        Assert.Equal("Consulta", reloaded.Service.Name);

        // Serviço desativado não aceita novas reservas.
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T11:00:00-03:00"), HttpStatusCode.UnprocessableEntity, ErrorCodes.ServiceInactive);
    }
}

/// <summary>US2 — expediente e exceções (FR-003, FR-004, FR-007).</summary>
public sealed class ScheduleEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceWorkingHours_ReturnsScheduleAndNoAffected()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        var schedule = new WeeklyScheduleDto(null, [new DayScheduleDto(DayOfWeek.Tuesday, [new TimeIntervalDto("08:00", "12:00"), new TimeIntervalDto("13:00", "18:00")])]);

        var result = await TestApi.ReadAsync<ScheduleChangeResultDto>(await client.PutAsJsonAsync("/api/v1/providers/me/schedule/working-hours", schedule, TestApi.Json), HttpStatusCode.OK);
        Assert.Empty(result.AffectedAppointments);
        Assert.Equal(2, Assert.Single(result.Schedule.Days).Intervals.Count);

        var reloaded = await TestApi.ReadAsync<WeeklyScheduleDto>(await client.GetAsync("/api/v1/providers/me/schedule"), HttpStatusCode.OK);
        Assert.Equal("America/Sao_Paulo", reloaded.TimeZoneId);
        Assert.Equal("08:00", reloaded.Days[0].Intervals[0].Start);
    }

    [Fact]
    public async Task OverlappingIntervals_Returns400()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        var schedule = new WeeklyScheduleDto(null, [new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto("08:00", "12:00"), new TimeIntervalDto("11:00", "14:00")])]);
        await ProblemAssert.IsProblemAsync(await client.PutAsJsonAsync("/api/v1/providers/me/schedule/working-hours", schedule, TestApi.Json), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Exceptions_CreateListAndDelete()
    {
        var (client, _) = await Api.RegisterProviderAsync();

        var full = await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.Closure, new DateOnly(2026, 12, 22), new DateOnly(2027, 1, 5), null, null, "Férias"), TestApi.Json);
        var created = await TestApi.ReadAsync<ScheduleExceptionCreatedDto>(full, HttpStatusCode.Created);
        Assert.Equal($"/api/v1/providers/me/schedule/exceptions/{created.Exception.Id}", full.Headers.Location?.OriginalString);

        await TestApi.EnsureAsync(await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.Closure, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), "15:00", "18:00", "Consulta médica"), TestApi.Json), HttpStatusCode.Created);
        await TestApi.EnsureAsync(await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.SpecialHours, new DateOnly(2026, 12, 19), new DateOnly(2026, 12, 19), "09:00", "13:00", "Plantão"), TestApi.Json), HttpStatusCode.Created);

        var november = await TestApi.ReadAsync<PagedResult<ScheduleExceptionDto>>(await client.GetAsync("/api/v1/providers/me/schedule/exceptions?from=2026-11-01&to=2026-11-30"), HttpStatusCode.OK);
        var partial = Assert.Single(november.Items);
        Assert.Equal("15:00", partial.StartTime);

        var all = await TestApi.ReadAsync<PagedResult<ScheduleExceptionDto>>(await client.GetAsync("/api/v1/providers/me/schedule/exceptions"), HttpStatusCode.OK);
        Assert.Equal(3, all.TotalCount);

        await TestApi.EnsureAsync(await client.DeleteAsync($"/api/v1/providers/me/schedule/exceptions/{created.Exception.Id}"), HttpStatusCode.NoContent);
        await ProblemAssert.IsProblemAsync(await client.DeleteAsync($"/api/v1/providers/me/schedule/exceptions/{created.Exception.Id}"), HttpStatusCode.NotFound, ErrorCodes.ScheduleExceptionNotFound);
    }

    [Fact]
    public async Task ExceptionInPast_Returns422()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        var response = await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.Closure, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null, null, null), TestApi.Json);
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.ScheduleExceptionInPast);
    }

    [Fact]
    public async Task OverlappingSpecialHours_Returns422()
    {
        var (client, _) = await Api.RegisterProviderAsync();
        var day = new DateOnly(2026, 12, 19);
        await TestApi.EnsureAsync(await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.SpecialHours, day, day, "09:00", "13:00", null), TestApi.Json), HttpStatusCode.Created);

        var response = await client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.SpecialHours, day, day, "12:00", "15:00", null), TestApi.Json);
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.ScheduleSpecialHoursOverlap);
    }

    [Fact]
    public async Task VacationOverBookedDate_ReportsAffectedWithoutCancelling()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        var response = await provider.Client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.Closure, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20), null, null, "Férias"), TestApi.Json);
        var created = await TestApi.ReadAsync<ScheduleExceptionCreatedDto>(response, HttpStatusCode.Created);

        var affected = Assert.Single(created.AffectedAppointments);
        Assert.Equal(booked.Id, affected.Id);

        var reloaded = await TestApi.ReadAsync<AppointmentDto>(await customer.Client.GetAsync($"/api/v1/appointments/{booked.Id}"), HttpStatusCode.OK);
        Assert.Equal(AppointmentStatus.Pending, reloaded.Status);
    }

    [Fact]
    public async Task ShrinkingWorkingHours_ReportsAffected()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T16:00:00-03:00");

        var morningsOnly = new WeeklyScheduleDto(null, [new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto("09:00", "12:00")])]);
        var result = await TestApi.ReadAsync<ScheduleChangeResultDto>(await provider.Client.PutAsJsonAsync("/api/v1/providers/me/schedule/working-hours", morningsOnly, TestApi.Json), HttpStatusCode.OK);

        Assert.Equal(booked.Id, Assert.Single(result.AffectedAppointments).Id);
    }
}
