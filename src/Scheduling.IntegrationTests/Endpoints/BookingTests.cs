using System.Net;
using System.Net.Http.Json;
using Scheduling.Application.Appointments;
using Scheduling.Application.Availability;
using Scheduling.Application.Providers;
using Scheduling.Application.Schedule;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

/// <summary>US1 — consulta de disponibilidade.</summary>
public sealed class AvailabilityEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Anonymous_GetsSlotsWithProviderOffset()
    {
        var provider = await Api.SetupProviderAsync();
        var response = await Api.Anonymous().GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date={TestApi.NextMonday}");
        var body = await TestApi.ReadAsync<AvailabilityResponse>(response, HttpStatusCode.OK);

        Assert.Equal("America/Sao_Paulo", body.TimeZoneId);
        Assert.Equal(30, body.DurationMinutes);
        Assert.Equal(new DateOnly(2026, 11, 16), body.Date);
        Assert.All(body.Slots, s => Assert.Equal(TimeSpan.FromHours(-3), s.StartAt.Offset));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"startAt\":\"2026-11-16T09:00:00-03:00\"", raw);

        var starts = body.Slots.Select(s => s.StartAt.ToString("HH:mm")).ToList();
        Assert.Equal("09:00", starts[0]);
        Assert.Equal("17:30", starts[^1]);
        Assert.Contains("11:30", starts);
        Assert.DoesNotContain(starts, s => string.CompareOrdinal(s, "11:30") > 0 && string.CompareOrdinal(s, "13:00") < 0);
    }

    [Fact]
    public async Task HolidayDate_ReturnsEmptySlots()
    {
        var provider = await Api.SetupProviderAsync();
        await TestApi.EnsureAsync(await provider.Client.PostAsJsonAsync("/api/v1/providers/me/schedule/exceptions",
            new ScheduleExceptionRequest(ScheduleExceptionType.Closure, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), null, null, "Feriado"), TestApi.Json), HttpStatusCode.Created);

        Assert.Empty(await TestApi.SlotStartsAsync(Api.Anonymous(), provider, TestApi.NextMonday));
    }

    [Fact]
    public async Task Weekend_ReturnsEmptySlots()
    {
        var provider = await Api.SetupProviderAsync();
        Assert.Empty(await TestApi.SlotStartsAsync(Api.Anonymous(), provider, "2026-11-15"));
    }

    [Fact]
    public async Task PastDateAndBeyondHorizon_Return422()
    {
        var provider = await Api.SetupProviderAsync();
        var anon = Api.Anonymous();

        await ProblemAssert.IsProblemAsync(
            await anon.GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date=2026-11-06"),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AvailabilityDateInPast);
        await ProblemAssert.IsProblemAsync(
            await anon.GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date=2027-01-09"),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AvailabilityBeyondHorizon);
    }

    [Fact]
    public async Task MalformedDate_Returns400WithDateError()
    {
        var provider = await Api.SetupProviderAsync();
        var response = await Api.Anonymous().GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date=16-11-2026");

        var problem = await ProblemAssert.IsProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.True(problem.GetProperty("errors").TryGetProperty("date", out _), problem.ToString());
    }

    [Fact]
    public async Task UnknownProviderOrForeignService_Return404()
    {
        var provider = await Api.SetupProviderAsync();
        var other = await Api.SetupProviderAsync(displayName: "Outro");
        var anon = Api.Anonymous();

        await ProblemAssert.IsProblemAsync(
            await anon.GetAsync($"/api/v1/providers/{Guid.NewGuid()}/availability?serviceId={provider.ServiceId}&date={TestApi.NextMonday}"),
            HttpStatusCode.NotFound, ErrorCodes.ProviderNotFound);
        await ProblemAssert.IsProblemAsync(
            await anon.GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={other.ServiceId}&date={TestApi.NextMonday}"),
            HttpStatusCode.NotFound, ErrorCodes.ServiceNotFound);
    }
}

/// <summary>US1 — criação de agendamentos.</summary>
public sealed class CreateAppointmentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_Returns201_WithLocationETagAndPendingStatus()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();

        var response = await TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00", notes: "Primeira consulta");
        var appointment = await TestApi.ReadAsync<AppointmentDto>(response, HttpStatusCode.Created);

        Assert.Equal($"/api/v1/appointments/{appointment.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal($"W/\"{appointment.Version}\"", response.Headers.ETag?.ToString());
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 10, 30, 0, TimeSpan.FromHours(-3)), appointment.EndAt);
        Assert.Equal("Primeira consulta", appointment.Notes);
        Assert.Equal(150m, appointment.Service.Price);
        Assert.Single(appointment.History);
    }

    [Fact]
    public async Task Create_WithAutoConfirm_IsConfirmed()
    {
        var provider = await Api.SetupProviderAsync(policy: new BookingPolicyDto(true, 15, 0, 60, 60, 120));
        var customer = await Api.RegisterCustomerAsync();

        Assert.Equal(AppointmentStatus.Confirmed, (await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00")).Status);
    }

    [Fact]
    public async Task BookedSlotAndOverlaps_VanishFromAvailability()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");

        var starts = await TestApi.SlotStartsAsync(Api.Anonymous(), provider, TestApi.NextMonday);
        Assert.DoesNotContain("09:45", starts);
        Assert.DoesNotContain("10:00", starts);
        Assert.DoesNotContain("10:15", starts);
        Assert.Contains("09:30", starts);
        Assert.Contains("10:30", starts);
    }

    [Fact]
    public async Task SecondCustomerSameSlot_Returns409()
    {
        var provider = await Api.SetupProviderAsync();
        var first = await Api.RegisterCustomerAsync();
        var second = await Api.RegisterCustomerAsync();
        await Api.BookOkAsync(first.Client, provider, "2026-11-16T10:00:00-03:00");

        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(second.Client, provider, "2026-11-16T10:15:00-03:00"),
            HttpStatusCode.Conflict, ErrorCodes.AppointmentSlotUnavailable);
    }

    [Fact]
    public async Task SameCustomerOverlapAtAnotherProvider_Returns409()
    {
        var a = await Api.SetupProviderAsync(displayName: "Prestador A");
        var b = await Api.SetupProviderAsync(displayName: "Prestador B");
        var customer = await Api.RegisterCustomerAsync();
        await Api.BookOkAsync(customer.Client, a, "2026-11-16T10:00:00-03:00");

        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, b, "2026-11-16T10:15:00-03:00"),
            HttpStatusCode.Conflict, ErrorCodes.AppointmentCustomerOverlap);
    }

    [Theory]
    [InlineData("2026-11-16T10:07:00-03:00", ErrorCodes.AvailabilityMisalignedStart)]
    [InlineData("2026-11-16T12:00:00-03:00", ErrorCodes.AvailabilityOutsideWorkingHours)]
    [InlineData("2026-11-15T10:00:00-03:00", ErrorCodes.AvailabilityOutsideWorkingHours)]
    [InlineData("2026-11-09T09:30:00-03:00", ErrorCodes.AvailabilityTooSoon)]
    [InlineData("2027-01-11T10:00:00-03:00", ErrorCodes.AvailabilityBeyondHorizon)]
    [InlineData("2026-11-06T10:00:00-03:00", ErrorCodes.AvailabilityDateInPast)]
    public async Task InvalidTimes_Return422WithSpecificCode(string startAt, string code)
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, provider, startAt), HttpStatusCode.UnprocessableEntity, code);
    }

    [Fact]
    public async Task SameInstantInUtc_IsAccepted()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var appointment = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T13:00:00Z");
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(-3)), appointment.StartAt);
    }

    [Fact]
    public async Task StartAtWithoutOffset_Returns400()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var problem = await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00"), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.True(problem.GetProperty("errors").TryGetProperty("startAt", out _));
    }

    [Fact]
    public async Task ProviderRole_Returns403_Anonymous_Returns401()
    {
        var provider = await Api.SetupProviderAsync();
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(provider.Client, provider, "2026-11-16T10:00:00-03:00"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(Api.Anonymous(), provider, "2026-11-16T10:00:00-03:00"), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);
    }

    [Fact]
    public async Task IdempotencyKey_ReplayReturns200SameAppointment_ReusedWithOtherPayloadReturns422()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var key = Guid.NewGuid();

        var first = await TestApi.ReadAsync<AppointmentDto>(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00", key), HttpStatusCode.Created);
        var replay = await TestApi.ReadAsync<AppointmentDto>(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00", key), HttpStatusCode.OK);
        Assert.Equal(first.Id, replay.Id);

        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T11:00:00-03:00", key),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task UnknownProvider_Returns404()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, Guid.NewGuid(), provider.ServiceId, "2026-11-16T10:00:00-03:00"),
            HttpStatusCode.NotFound, ErrorCodes.ProviderNotFound);
    }
}

/// <summary>US1 — leitura de agendamento com controle de propriedade.</summary>
public sealed class GetAppointmentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OwnerProviderAndAdmin_See_OthersGet404()
    {
        var provider = await Api.SetupProviderAsync();
        var otherProvider = await Api.SetupProviderAsync(displayName: "Outro");
        var owner = await Api.RegisterCustomerAsync();
        var stranger = await Api.RegisterCustomerAsync();
        var appointment = await Api.BookOkAsync(owner.Client, provider, "2026-11-16T10:00:00-03:00");
        var url = $"/api/v1/appointments/{appointment.Id}";

        var asOwner = await owner.Client.GetAsync(url);
        var dto = await TestApi.ReadAsync<AppointmentDto>(asOwner, HttpStatusCode.OK);
        Assert.NotNull(asOwner.Headers.ETag);
        Assert.Single(dto.History);

        var asProvider = await TestApi.ReadAsync<AppointmentDto>(await provider.Client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal("+5511988887777", asProvider.Customer.Phone);

        await TestApi.EnsureAsync(await (await Api.AdminAsync()).GetAsync(url), HttpStatusCode.OK);
        await ProblemAssert.IsProblemAsync(await stranger.Client.GetAsync(url), HttpStatusCode.NotFound, ErrorCodes.AppointmentNotFound);
        await ProblemAssert.IsProblemAsync(await otherProvider.Client.GetAsync(url), HttpStatusCode.NotFound, ErrorCodes.AppointmentNotFound);
    }
}
