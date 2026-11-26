using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options, TimeProvider timeProvider)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();

    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<AppointmentStatusChange> AppointmentStatusChanges => Set<AppointmentStatusChange>();

    public DbSet<Notification> Notifications => Set<Notification>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => await SaveChangesAsync(cancellationToken);

    async Task<T> IUnitOfWork.ExecuteSerializedAsync<T>(Guid lockKey, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        // Lock consultivo liberado automaticamente no commit/rollback. Colisões de hash só causam serialização extra.
        var key = BitConverter.ToInt64(lockKey.ToByteArray(), 0);
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ErrorCodes.AppointmentConcurrencyConflict, "O registro foi alterado por outra operação. Recarregue e tente novamente.");
        }
        catch (DbUpdateException ex) when (PostgresErrorTranslator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums persistidos como texto (legível e estável) — data-model.md.
        configurationBuilder.Properties<Role>().HaveConversion<string>();
        configurationBuilder.Properties<ServiceCategory>().HaveConversion<string>();
        configurationBuilder.Properties<ScheduleExceptionType>().HaveConversion<string>();
        configurationBuilder.Properties<AppointmentStatus>().HaveConversion<string>();
        configurationBuilder.Properties<NotificationType>().HaveConversion<string>();
        configurationBuilder.Properties<NotificationStatus>().HaveConversion<string>();
        configurationBuilder.Properties<NotificationChannel>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);
        SnakeCaseNaming.Apply(modelBuilder);
    }

    private void StampAuditFields()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetCreated(now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.SetUpdated(now);
            }
        }
    }
}
