using System.Net;
using System.Text;
using Scheduling.Domain.Common;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Authorization;

/// <summary>
/// SC-007: matriz de autorização por perfil sobre todas as operações protegidas do contrato (x-roles).
/// Anônimo → 401; perfil fora da política → 403. O acesso cruzado por propriedade é coberto nos testes de cada recurso.
/// </summary>
public sealed class AuthorizationMatrixTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Id = "00000000-0000-7000-8000-000000000001";

    public static TheoryData<string, string, string> ProtectedOperations => new()
    {
        // método, caminho, perfis permitidos ("Any" = qualquer autenticado)
        { "GET", "/api/v1/auth/me", "Any" },
        { "GET", "/api/v1/customers/me", "Customer" },
        { "PUT", "/api/v1/customers/me", "Customer" },
        { "GET", "/api/v1/customers/me/appointments", "Customer" },
        { "GET", "/api/v1/providers/me", "Provider" },
        { "PUT", "/api/v1/providers/me", "Provider" },
        { "PUT", "/api/v1/providers/me/booking-policy", "Provider" },
        { "GET", "/api/v1/providers/me/appointments", "Provider" },
        { "GET", "/api/v1/providers/me/services", "Provider" },
        { "POST", "/api/v1/providers/me/services", "Provider" },
        { "PUT", $"/api/v1/providers/me/services/{Id}", "Provider" },
        { "DELETE", $"/api/v1/providers/me/services/{Id}", "Provider" },
        { "PATCH", $"/api/v1/providers/me/services/{Id}/status", "Provider" },
        { "GET", "/api/v1/providers/me/schedule", "Provider" },
        { "PUT", "/api/v1/providers/me/schedule/working-hours", "Provider" },
        { "GET", "/api/v1/providers/me/schedule/exceptions", "Provider" },
        { "POST", "/api/v1/providers/me/schedule/exceptions", "Provider" },
        { "DELETE", $"/api/v1/providers/me/schedule/exceptions/{Id}", "Provider" },
        { "POST", "/api/v1/appointments", "Customer" },
        { "GET", $"/api/v1/appointments/{Id}", "Any" },
        { "POST", $"/api/v1/appointments/{Id}/confirm", "Provider|Admin" },
        { "POST", $"/api/v1/appointments/{Id}/cancel", "Any" },
        { "POST", $"/api/v1/appointments/{Id}/complete", "Provider|Admin" },
        { "GET", "/api/v1/notifications/me", "Any" },
        { "GET", "/api/v1/admin/users", "Admin" },
        { "PATCH", $"/api/v1/admin/users/{Id}/status", "Admin" },
        { "GET", "/api/v1/admin/notifications", "Admin" },
    };

    public static TheoryData<string> PublicOperations => new()
    {
        "/api/v1/providers",
        $"/api/v1/providers/{Id}",
        $"/api/v1/providers/{Id}/services",
        $"/api/v1/providers/{Id}/availability?serviceId={Id}&date=2026-11-16",
        "/openapi/v1.json",
    };

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task ProtectedOperation_EnforcesRoles(string method, string path, string allowed)
    {
        var clients = new Dictionary<string, HttpClient>
        {
            ["Customer"] = (await Api.RegisterCustomerAsync()).Client,
            ["Provider"] = (await Api.RegisterProviderAsync()).Client,
            ["Admin"] = await Api.AdminAsync(),
        };

        await ProblemAssert.IsProblemAsync(await SendAsync(Api.Anonymous(), method, path), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);

        foreach (var (role, client) in clients)
        {
            var response = await SendAsync(client, method, path);
            if (allowed == "Any" || allowed.Split('|').Contains(role))
            {
                Assert.True(
                    response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                    $"{role} deveria ter acesso a {method} {path}, obteve {(int)response.StatusCode}.");
            }
            else
            {
                await ProblemAssert.IsProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PublicOperations))]
    public async Task PublicOperation_AllowsAnonymous(string path)
    {
        var response = await Api.Anonymous().GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }
}
