using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application.Abstractions;
using Scheduling.Infrastructure.Identity;
using Scheduling.Infrastructure.Notifications;
using Scheduling.Infrastructure.Persistence;
using Scheduling.Infrastructure.Persistence.Repositories;

namespace Scheduling.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada. Defina ConnectionStrings:{ConnectionStringName} "
                + "(appsettings.Development.json em desenvolvimento ou a variável ConnectionStrings__DefaultConnection).");
        }

        services.AddDbContext<SchedulingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SchedulingDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinSigningKeyBytes, $"Jwt:SigningKey deve ter ao menos {JwtOptions.MinSigningKeyBytes} bytes.")
            .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes deve ser positivo.")
            .ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(o => o.MaxAttempts is >= 1 and <= 3, "Notifications:MaxAttempts deve estar entre 1 e 3.")
            .Validate(o => o.ReminderIntervalSeconds > 0 && o.DispatchIntervalSeconds > 0 && o.BatchSize > 0, "Intervalos e lote de notificações devem ser positivos.")
            .ValidateOnStart();
        services.AddSingleton<INotificationChannel, SimulatedNotificationChannel>();
        services.AddScoped<IReminderProcessor, ReminderProcessor>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        if (configuration.GetValue("Notifications:Enabled", true))
        {
            services.AddHostedService<ReminderSchedulerService>();
            services.AddHostedService<NotificationDispatcherService>();
        }

        return services;
    }
}
