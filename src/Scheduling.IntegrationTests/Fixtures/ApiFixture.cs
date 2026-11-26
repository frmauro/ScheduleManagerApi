using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Scheduling.Infrastructure.Seed;

namespace Scheduling.IntegrationTests.Fixtures;

/// <summary>Relógio controlável (pode avançar e voltar) usado no lugar de <see cref="TimeProvider.System"/>.</summary>
public sealed class TestClock : TimeProvider
{
    /// <summary>Segunda-feira 2026-11-09 12:00Z = 09:00 em São Paulo.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; } = Start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}

public sealed class ApiFactory(string connectionString, TestClock clock) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@tests.local";
    public const string AdminPassword = "Admin12345";
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Notifications:Enabled", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
    }
}

/// <summary>
/// Cria um banco PostgreSQL descartável e exclusivo por execução (nunca o banco <c>postgres</c> de desenvolvimento),
/// sobe a API via <see cref="WebApplicationFactory{TEntryPoint}"/> e remove o banco ao final.
/// Servidor configurável por <c>SCHEDULEMANAGER_TEST_DB</c> (padrão: localhost:5432, postgres/123).
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string ServerEnvironmentVariable = "SCHEDULEMANAGER_TEST_DB";
    private const string DefaultServer = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123";

    private string _adminConnectionString = string.Empty;
    private string _databaseName = string.Empty;

    public TestClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ServerEnvironmentVariable) ?? DefaultServer);
        _adminConnectionString = server.ConnectionString;
        _databaseName = $"schedulemanager_it_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName, MaxPoolSize = 200 }.ConnectionString;
        Factory = new ApiFactory(ConnectionString, Clock);
        _ = Factory.Server; // inicia o host: aplica migrations e provisiona o Admin
    }

    /// <summary>Estado limpo para cada teste: relógio no início e tabelas vazias (Admin reprovisionado).</summary>
    public async Task ResetAsync()
    {
        Clock.Now = TestClock.Start;
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "TRUNCATE notifications, appointment_status_changes, appointments, services, provider_schedule_exceptions, "
                + "provider_working_hours, providers, customers, users CASCADE",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        await DatabaseInitializer.InitializeAsync(Factory.Services);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>Base dos testes de integração: estado limpo por teste e helpers de API.</summary>
[Collection(ApiCollection.Name)]
public abstract class IntegrationTestBase(ApiFixture fixture) : IAsyncLifetime
{
    protected ApiFixture Fixture { get; } = fixture;

    protected TestApi Api { get; } = new(fixture);

    protected TestClock Clock => Fixture.Clock;

    public virtual async ValueTask InitializeAsync() => await Fixture.ResetAsync();

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
