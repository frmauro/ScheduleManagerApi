using System.Net;
using System.Net.Http.Json;
using Scheduling.Application.Auth;
using Scheduling.Application.Providers;
using Scheduling.Application.Services;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

public sealed class AuthEndpointsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RegisterCustomer_Returns201WithToken()
    {
        var customer = await Api.RegisterCustomerAsync();

        Assert.False(string.IsNullOrWhiteSpace(customer.Auth.AccessToken));
        Assert.Equal("Bearer", customer.Auth.TokenType);
        Assert.Equal(Role.Customer, customer.Auth.User.Role);
        Assert.Equal(Clock.Now.AddMinutes(60), customer.Auth.ExpiresAt);
    }

    [Fact]
    public async Task RegisterProvider_Returns201WithDefaultPolicy()
    {
        var (client, auth) = await Api.RegisterProviderAsync();
        Assert.Equal(Role.Provider, auth.User.Role);
        Assert.NotNull(auth.User.ProviderId);

        var profile = await TestApi.ReadAsync<ProviderProfileDto>(await client.GetAsync("/api/v1/providers/me"), HttpStatusCode.OK);
        Assert.Equal(new BookingPolicyDto(false, 15, 0, 60, 60, 120), profile.BookingPolicy);
        Assert.Equal("America/Sao_Paulo", profile.TimeZoneId);
    }

    [Fact]
    public async Task Register_DuplicateEmailIgnoringCase_Returns409()
    {
        await Api.RegisterCustomerAsync("maria@exemplo.com");
        var response = await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/register/customer",
            new RegisterCustomerRequest("Maria 2", "MARIA@Exemplo.com", "Senha123", "+5511988887777"), TestApi.Json);

        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.UserEmailTaken);
    }

    [Fact]
    public async Task Register_InvalidPayload_Returns400WithFieldErrors()
    {
        var response = await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/register/customer",
            new RegisterCustomerRequest("M", "invalido", "curta", "123"), TestApi.Json);

        var problem = await ProblemAssert.IsProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "name", "email", "password", "phone" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Campo '{field}' ausente em errors: {errors}");
        }
    }

    [Fact]
    public async Task Register_MalformedJson_Returns400()
    {
        var response = await Api.Anonymous().PostAsync("/api/v1/auth/register/customer",
            new StringContent("{ \"name\": ", System.Text.Encoding.UTF8, "application/json"));
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnIdenticalGeneric401()
    {
        await Api.RegisterCustomerAsync("joao@exemplo.com");

        var wrongPassword = await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("joao@exemplo.com", "Errada123"), TestApi.Json);
        var unknown = await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ninguem@exemplo.com", "Errada123"), TestApi.Json);

        var a = await ProblemAssert.IsProblemAsync(wrongPassword, HttpStatusCode.Unauthorized, ErrorCodes.AuthInvalidCredentials);
        var b = await ProblemAssert.IsProblemAsync(unknown, HttpStatusCode.Unauthorized, ErrorCodes.AuthInvalidCredentials);
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnEmail()
    {
        await Api.RegisterCustomerAsync("ana@exemplo.com");
        var response = await Api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ANA@exemplo.com", "Senha123"), TestApi.Json);
        var auth = await TestApi.ReadAsync<AuthResponse>(response, HttpStatusCode.OK);
        Assert.NotNull(auth.User.CustomerId);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var customer = await Api.RegisterCustomerAsync();
        Clock.Advance(TimeSpan.FromMinutes(61));

        await ProblemAssert.IsProblemAsync(await customer.Client.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);
    }

    [Fact]
    public async Task MissingOrInvalidToken_Returns401()
    {
        await ProblemAssert.IsProblemAsync(await Api.Anonymous().GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);
        await ProblemAssert.IsProblemAsync(await Api.Authenticated("abc.def.ghi").GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);
    }

    [Fact]
    public async Task Me_ReturnsRoleAndProfileIds()
    {
        var customer = await Api.RegisterCustomerAsync();
        var me = await TestApi.ReadAsync<CurrentUserDto>(await customer.Client.GetAsync("/api/v1/auth/me"), HttpStatusCode.OK);

        Assert.Equal(Role.Customer, me.Role);
        Assert.Equal(customer.CustomerId, me.CustomerId);
        Assert.Null(me.ProviderId);
    }

    [Fact]
    public async Task CustomerCannotCallProviderOperations_Returns403()
    {
        var customer = await Api.RegisterCustomerAsync();
        var response = await customer.Client.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("X", null, 30, 10m), TestApi.Json);
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
    }
}
