using System.Net;
using System.Net.Http.Json;
using Scheduling.Application.Admin;
using Scheduling.Application.Appointments;
using Scheduling.Application.Auth;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Customers;
using Scheduling.Application.Providers;
using Scheduling.Application.Services;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

/// <summary>US4 — perfil e histórico do cliente.</summary>
public sealed class CustomersEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAndUpdateProfile_EmailChangeAffectsLogin()
    {
        var customer = await Api.RegisterCustomerAsync("antigo@exemplo.com", "Maria");

        var profile = await TestApi.ReadAsync<CustomerProfileDto>(await customer.Client.GetAsync("/api/v1/customers/me"), HttpStatusCode.OK);
        Assert.Equal("Maria", profile.Name);

        var updated = await TestApi.ReadAsync<CustomerProfileDto>(
            await customer.Client.PutAsJsonAsync("/api/v1/customers/me", new UpdateCustomerRequest("Maria Souza", "Novo@Exemplo.com", "+5511977776666"), TestApi.Json), HttpStatusCode.OK);
        Assert.Equal("novo@exemplo.com", updated.Email);

        await TestApi.EnsureAsync(await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("novo@exemplo.com", "Senha123"), TestApi.Json), HttpStatusCode.OK);
        await TestApi.EnsureAsync(await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("antigo@exemplo.com", "Senha123"), TestApi.Json), HttpStatusCode.Unauthorized);

        var me = await TestApi.ReadAsync<CurrentUserDto>(await customer.Client.GetAsync("/api/v1/auth/me"), HttpStatusCode.OK);
        Assert.Equal("Maria Souza", me.Name);
    }

    [Fact]
    public async Task UpdateToTakenEmail_Returns409()
    {
        await Api.RegisterCustomerAsync("ocupado@exemplo.com");
        var customer = await Api.RegisterCustomerAsync();

        var response = await customer.Client.PutAsJsonAsync("/api/v1/customers/me", new UpdateCustomerRequest("Maria", "ocupado@exemplo.com", "+5511977776666"), TestApi.Json);
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.UserEmailTaken);
    }

    [Fact]
    public async Task History_OnlyOwn_FiltersAndDefaultDescendingSort()
    {
        var a = await Api.SetupProviderAsync(displayName: "Prestador A");
        var b = await Api.SetupProviderAsync(displayName: "Prestador B");
        var customer = await Api.RegisterCustomerAsync();
        var other = await Api.RegisterCustomerAsync();

        var first = await Api.BookOkAsync(customer.Client, a, "2026-11-16T09:00:00-03:00");
        await Api.BookOkAsync(customer.Client, b, "2026-11-17T09:00:00-03:00");
        await Api.BookOkAsync(customer.Client, a, "2026-11-18T09:00:00-03:00");
        await Api.BookOkAsync(other.Client, a, "2026-11-19T09:00:00-03:00");
        await TestApi.EnsureAsync(await TestApi.PostActionAsync(customer.Client, first.Id, "cancel", new CancelAppointmentRequest("Desisti", null)), HttpStatusCode.OK);

        var all = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(await customer.Client.GetAsync("/api/v1/customers/me/appointments"), HttpStatusCode.OK);
        Assert.Equal(3, all.TotalCount);
        Assert.All(all.Items, i => Assert.Equal(customer.CustomerId, i.CustomerId));
        Assert.Equal(all.Items.OrderByDescending(i => i.StartAt).Select(i => i.Id), all.Items.Select(i => i.Id));

        var cancelled = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await customer.Client.GetAsync("/api/v1/customers/me/appointments?status=CancelledByCustomer"), HttpStatusCode.OK);
        Assert.Equal(first.Id, Assert.Single(cancelled.Items).Id);

        var atB = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await customer.Client.GetAsync($"/api/v1/customers/me/appointments?providerId={b.ProviderId}"), HttpStatusCode.OK);
        Assert.Equal("Prestador B", Assert.Single(atB.Items).ProviderName);

        var ranged = await TestApi.ReadAsync<PagedResult<AppointmentSummaryDto>>(
            await customer.Client.GetAsync("/api/v1/customers/me/appointments?from=2026-11-17T00:00:00Z&to=2026-11-30T00:00:00Z&sort=startAt&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(2, ranged.TotalCount);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.FromHours(-3)), Assert.Single(ranged.Items).StartAt);
    }

    [Fact]
    public async Task ProviderCannotAccessCustomerEndpoints()
    {
        var (provider, _) = await Api.RegisterProviderAsync();
        await ProblemAssert.IsProblemAsync(await provider.GetAsync("/api/v1/customers/me"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
    }
}

/// <summary>US4 — administração.</summary>
public sealed class AdminEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ListUsers_WithFilters()
    {
        await Api.RegisterCustomerAsync(name: "Carla Cliente");
        await Api.RegisterProviderAsync();
        var admin = await Api.AdminAsync();

        var customers = await TestApi.ReadAsync<PagedResult<UserSummaryDto>>(await admin.GetAsync("/api/v1/admin/users?role=Customer"), HttpStatusCode.OK);
        Assert.Equal(Role.Customer, Assert.Single(customers.Items).Role);

        var byText = await TestApi.ReadAsync<PagedResult<UserSummaryDto>>(await admin.GetAsync("/api/v1/admin/users?q=carla"), HttpStatusCode.OK);
        Assert.Equal("Carla Cliente", Assert.Single(byText.Items).Name);

        var all = await TestApi.ReadAsync<PagedResult<UserSummaryDto>>(await admin.GetAsync("/api/v1/admin/users?isActive=true"), HttpStatusCode.OK);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public async Task DeactivatingProvider_HidesFromCatalogBlocksBookingAndLogin()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var admin = await Api.AdminAsync();

        await TestApi.EnsureAsync(await admin.PatchAsJsonAsync($"/api/v1/admin/users/{provider.Auth.User.Id}/status", new ActiveStatusRequest(false), TestApi.Json), HttpStatusCode.NoContent);

        var catalog = await TestApi.ReadAsync<PagedResult<ProviderSummaryDto>>(await Api.Anonymous().GetAsync("/api/v1/providers"), HttpStatusCode.OK);
        Assert.Empty(catalog.Items);
        await ProblemAssert.IsProblemAsync(await Api.Anonymous().GetAsync($"/api/v1/providers/{provider.ProviderId}"), HttpStatusCode.NotFound, ErrorCodes.ProviderNotFound);
        await ProblemAssert.IsProblemAsync(
            await Api.Anonymous().GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date={TestApi.NextMonday}"),
            HttpStatusCode.NotFound, ErrorCodes.ProviderNotFound);
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00"), HttpStatusCode.UnprocessableEntity, ErrorCodes.ProviderInactive);
        await ProblemAssert.IsProblemAsync(
            await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(provider.Auth.User.Email, "Senha123"), TestApi.Json),
            HttpStatusCode.Unauthorized, ErrorCodes.AuthInvalidCredentials);

        // Reativação restaura o catálogo.
        await TestApi.EnsureAsync(await admin.PatchAsJsonAsync($"/api/v1/admin/users/{provider.Auth.User.Id}/status", new ActiveStatusRequest(true), TestApi.Json), HttpStatusCode.NoContent);
        var restored = await TestApi.ReadAsync<PagedResult<ProviderSummaryDto>>(await Api.Anonymous().GetAsync("/api/v1/providers"), HttpStatusCode.OK);
        Assert.Single(restored.Items);
    }

    [Fact]
    public async Task SelfDeactivation_Returns422_UnknownUser_Returns404()
    {
        var admin = await Api.AdminAsync();
        var me = await TestApi.ReadAsync<CurrentUserDto>(await admin.GetAsync("/api/v1/auth/me"), HttpStatusCode.OK);

        await ProblemAssert.IsProblemAsync(await admin.PatchAsJsonAsync($"/api/v1/admin/users/{me.Id}/status", new ActiveStatusRequest(false), TestApi.Json),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.AdminCannotDeactivateSelf);
        await ProblemAssert.IsProblemAsync(await admin.PatchAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/status", new ActiveStatusRequest(false), TestApi.Json),
            HttpStatusCode.NotFound, ErrorCodes.UserNotFound);
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var customer = await Api.RegisterCustomerAsync();
        await ProblemAssert.IsProblemAsync(await customer.Client.GetAsync("/api/v1/admin/users"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
    }
}
