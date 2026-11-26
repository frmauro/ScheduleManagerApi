using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Endpoints;

/// <summary>SC-006 / FR-028: todas as respostas de erro seguem RFC 7807 com code e traceId, sem detalhes internos.</summary>
public sealed class ProblemDetailsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EveryErrorStatus_UsesProblemDetailsShape()
    {
        var provider = await Api.SetupProviderAsync();
        var customer = await Api.RegisterCustomerAsync();
        var booked = await Api.BookOkAsync(customer.Client, provider, "2026-11-16T10:00:00-03:00");
        var other = await Api.RegisterCustomerAsync();

        await ProblemAssert.IsProblemAsync(await Api.Anonymous().GetAsync("/api/v1/providers?pageSize=0"), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await ProblemAssert.IsProblemAsync(await Api.Anonymous().GetAsync("/api/v1/customers/me"), HttpStatusCode.Unauthorized, ErrorCodes.AuthUnauthenticated);
        await ProblemAssert.IsProblemAsync(await customer.Client.GetAsync("/api/v1/admin/users"), HttpStatusCode.Forbidden, ErrorCodes.AuthForbidden);
        await ProblemAssert.IsProblemAsync(await other.Client.GetAsync($"/api/v1/appointments/{booked.Id}"), HttpStatusCode.NotFound, ErrorCodes.AppointmentNotFound);
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(other.Client, provider, "2026-11-16T10:00:00-03:00"), HttpStatusCode.Conflict, ErrorCodes.AppointmentSlotUnavailable);
        await ProblemAssert.IsProblemAsync(await TestApi.PostActionAsync(provider.Client, booked.Id, "confirm", ifMatch: "W/\"1\""), HttpStatusCode.PreconditionFailed, ErrorCodes.AppointmentPreconditionFailed);
        await ProblemAssert.IsProblemAsync(await TestApi.BookAsync(other.Client, provider, "2026-11-16T10:07:00-03:00"), HttpStatusCode.UnprocessableEntity, ErrorCodes.AvailabilityMisalignedStart);
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        var response = await Api.Anonymous().GetAsync("/api/v1/does-not-exist");
        await ProblemAssert.IsProblemAsync(response, HttpStatusCode.NotFound, "route.not_found");
    }

    [Fact]
    public async Task UnhandledException_InProduction_Returns500WithoutInternalDetails()
    {
        using var factory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProviderRepository>();
                services.AddScoped<IProviderRepository, ExplodingProviderRepository>();
            });
        });

        var response = await factory.CreateClient().GetAsync("/api/v1/providers");
        var problem = await ProblemAssert.IsProblemAsync(response, HttpStatusCode.InternalServerError, ErrorCodes.InternalError);
        var raw = problem.GetRawText();

        Assert.DoesNotContain("segredo-interno", raw);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.True(!problem.TryGetProperty("detail", out var detail) || detail.ValueKind == JsonValueKind.Null);
    }

    private sealed class ExplodingProviderRepository : IProviderRepository
    {
        private static Exception Boom() => new InvalidOperationException("segredo-interno: falha simulada");

        public Task<Provider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw Boom();

        public Task<Provider?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw Boom();

        public Task<Provider?> GetWithScheduleAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw Boom();

        public Task<PagedResult<Provider>> SearchActiveAsync(ProviderSearch search, PageQuery page, CancellationToken cancellationToken = default) => throw Boom();

        public Task<PagedResult<ScheduleException>> ListExceptionsAsync(Guid providerId, DateOnly? from, DateOnly? to, PageQuery page, CancellationToken cancellationToken = default) => throw Boom();

        public void Add(Provider provider) => throw Boom();
    }
}
