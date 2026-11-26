using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scheduling.Application.Appointments;
using Scheduling.Application.Auth;
using Scheduling.Application.Providers;
using Scheduling.Application.Schedule;
using Scheduling.Application.Services;
using Scheduling.Domain.Providers;

namespace Scheduling.IntegrationTests.Fixtures;

public sealed record ProviderSetup(HttpClient Client, AuthResponse Auth, Guid ProviderId, Guid ServiceId);

public sealed record CustomerSetup(HttpClient Client, AuthResponse Auth, Guid CustomerId);

/// <summary>Helpers de alto nível sobre a API real (pipeline HTTP completo).</summary>
public sealed class TestApi(ApiFixture fixture)
{
    public const string NextMonday = "2026-11-16";

    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private int _sequence;

    public static AddressDto Address { get; } = new("Rua das Flores", "100", null, "Centro", "São Paulo", "SP", "01001000");

    /// <summary>Expediente padrão: segunda a sexta 09–12 e 13–18.</summary>
    public static WeeklyScheduleDto DefaultSchedule { get; } = new(
        null,
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(d => new DayScheduleDto(d, [new TimeIntervalDto("09:00", "12:00"), new TimeIntervalDto("13:00", "18:00")]))
            .ToList());

    public HttpClient Anonymous() => fixture.Factory.CreateClient();

    public HttpClient Authenticated(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public string UniqueEmail(string prefix) => $"{prefix}{Interlocked.Increment(ref _sequence)}-{Guid.NewGuid():N}@tests.local";

    public async Task<CustomerSetup> RegisterCustomerAsync(string? email = null, string name = "Cliente Teste")
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register/customer",
            new RegisterCustomerRequest(name, email ?? UniqueEmail("cliente"), "Senha123", "+5511988887777"), Json);
        var auth = await ReadAsync<AuthResponse>(response, HttpStatusCode.Created);
        return new CustomerSetup(Authenticated(auth.AccessToken), auth, auth.User.CustomerId!.Value);
    }

    public async Task<(HttpClient Client, AuthResponse Auth)> RegisterProviderAsync(string? email = null, string displayName = "Clínica Teste", ServiceCategory category = ServiceCategory.Health)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register/provider",
            new RegisterProviderRequest("Dona Clínica", email ?? UniqueEmail("prestador"), "Senha123", displayName, "Atendimento", category, null, "+5511999998888", Address, null), Json);
        var auth = await ReadAsync<AuthResponse>(response, HttpStatusCode.Created);
        return (Authenticated(auth.AccessToken), auth);
    }

    public Task<HttpClient> AdminAsync() => LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    /// <summary>Novo login (útil depois de avançar o relógio além da validade do token).</summary>
    public async Task<HttpClient> LoginAsync(string email, string password = "Senha123")
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), Json);
        var auth = await ReadAsync<AuthResponse>(response, HttpStatusCode.OK);
        return Authenticated(auth.AccessToken);
    }

    /// <summary>Prestador com um serviço e expediente padrão (seg–sex 09–12/13–18).</summary>
    public async Task<ProviderSetup> SetupProviderAsync(int durationMinutes = 30, BookingPolicyDto? policy = null, string displayName = "Clínica Teste")
    {
        var (client, auth) = await RegisterProviderAsync(displayName: displayName);
        if (policy is not null)
        {
            await EnsureAsync(await client.PutAsJsonAsync("/api/v1/providers/me/booking-policy", policy, Json), HttpStatusCode.OK);
        }

        await EnsureAsync(await client.PutAsJsonAsync("/api/v1/providers/me/schedule/working-hours", DefaultSchedule, Json), HttpStatusCode.OK);
        var service = await ReadAsync<ServiceDto>(
            await client.PostAsJsonAsync("/api/v1/providers/me/services", new ServiceRequest("Consulta", "Consulta padrão", durationMinutes, 150m), Json),
            HttpStatusCode.Created);

        return new ProviderSetup(client, auth, auth.User.ProviderId!.Value, service.Id);
    }

    public static Task<HttpResponseMessage> BookAsync(HttpClient client, ProviderSetup provider, string startAt, Guid? idempotencyKey = null, string? notes = null) =>
        BookAsync(client, provider.ProviderId, provider.ServiceId, startAt, idempotencyKey, notes);

    public static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid providerId, Guid serviceId, string startAt, Guid? idempotencyKey = null, string? notes = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/appointments")
        {
            Content = JsonContent.Create(new CreateAppointmentRequest(providerId, serviceId, startAt, notes), options: Json),
        };
        if (idempotencyKey is { } key)
        {
            request.Headers.Add("Idempotency-Key", key.ToString());
        }

        return client.SendAsync(request);
    }

    public async Task<AppointmentDto> BookOkAsync(HttpClient client, ProviderSetup provider, string startAt) =>
        await ReadAsync<AppointmentDto>(await BookAsync(client, provider, startAt), HttpStatusCode.Created);

    public static Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid appointmentId, string action, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/appointments/{appointmentId}/{action}")
        {
            Content = body is null ? null : JsonContent.Create(body, options: Json),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    public static async Task<List<string>> SlotStartsAsync(HttpClient client, ProviderSetup provider, string date)
    {
        var response = await client.GetAsync($"/api/v1/providers/{provider.ProviderId}/availability?serviceId={provider.ServiceId}&date={date}");
        var body = await ReadAsync<Scheduling.Application.Availability.AvailabilityResponse>(response, HttpStatusCode.OK);
        return body.Slots.Select(s => s.StartAt.ToString("HH:mm")).ToList();
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        await EnsureAsync(response, expected);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Esperado {(int)expected} {expected}, obtido {(int)response.StatusCode} {response.StatusCode}. "
                + $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}. Corpo: {body}");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Asserções de respostas RFC 7807.</summary>
public static class ProblemAssert
{
    public static async Task<JsonElement> IsProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Esperado {(int)status}, obtido {(int)response.StatusCode}. Corpo: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("title").GetString()));
        Assert.EndsWith(code, json.GetProperty("type").GetString());
        return json;
    }
}
