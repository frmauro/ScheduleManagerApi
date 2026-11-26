using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure.Seed;

/// <summary>
/// Inicialização opcional: aplica migrations (<c>Database:ApplyMigrationsOnStartup</c>) e provisiona o Admin inicial
/// (<c>Seed:AdminEmail</c>/<c>Seed:AdminPassword</c>), de forma idempotente (FR-011).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var db = provider.GetRequiredService<SchedulingDbContext>();

        if (configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalized, cancellationToken))
        {
            return;
        }

        var admin = User.Create("Administrador", normalized, Role.Admin);
        admin.SetPasswordHash(provider.GetRequiredService<IPasswordHasher>().Hash(admin, password));
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Usuário Admin inicial provisionado.");
    }
}
