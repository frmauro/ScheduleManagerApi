using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application.Admin;
using Scheduling.Application.Appointments;
using Scheduling.Application.Auth;
using Scheduling.Application.Availability;
using Scheduling.Application.Customers;
using Scheduling.Application.Notifications;
using Scheduling.Application.Providers;
using Scheduling.Application.Schedule;
using Scheduling.Application.Services;

namespace Scheduling.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Interrompe cada regra na primeira falha: evita avaliar regras dependentes sobre valores nulos/inválidos.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: false);

        services.AddScoped<AuthService>();
        services.AddScoped<ProviderService>();
        services.AddScoped<ServiceCatalogService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<NotificationQueryService>();

        return services;
    }
}
