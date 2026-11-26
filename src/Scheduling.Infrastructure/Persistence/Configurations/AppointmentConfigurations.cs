using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.OwnsOne(a => a.Service, snapshot =>
        {
            snapshot.Property(s => s.Name).HasMaxLength(ValidationLimits.ServiceNameMaxLength).IsRequired();
            snapshot.Property(s => s.DurationMinutes).HasColumnType("smallint");
            snapshot.Property(s => s.Price).HasColumnType("numeric(10,2)");
        });
        builder.Navigation(a => a.Service).IsRequired();

        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.CustomerNotes).HasMaxLength(ValidationLimits.NotesMaxLength);
        builder.Property(a => a.CancellationReason).HasMaxLength(ValidationLimits.CancellationReasonMaxLength);
        // Coluna de sistema xmin do PostgreSQL como token de concorrência otimista (research R-02).
        builder.Property(a => a.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasOne<Provider>().WithMany().HasForeignKey(a => a.ProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.History).WithOne().HasForeignKey(h => h.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.CustomerId, a.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName(Constraints.AppointmentsIdempotency);
        builder.HasIndex(a => new { a.CustomerId, a.StartAt }).IsDescending(false, true).HasDatabaseName("ix_appointments_customer_start");
        builder.HasIndex(a => new { a.ProviderId, a.StartAt }).HasDatabaseName("ix_appointments_provider_start");
        builder.HasIndex(a => new { a.Status, a.StartAt }).HasDatabaseName("ix_appointments_status_start");
        builder.HasIndex(a => a.ServiceId).HasDatabaseName("ix_appointments_service");

        // ck_appointments_time e as exclusion constraints GiST são criadas via SQL na migration (research R-01).
    }
}

internal sealed class AppointmentStatusChangeConfiguration : IEntityTypeConfiguration<AppointmentStatusChange>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusChange> builder)
    {
        builder.ToTable("appointment_status_changes");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Reason).HasMaxLength(ValidationLimits.CancellationReasonMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => h.AppointmentId).HasDatabaseName("ix_status_changes_appointment");
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", t => t.HasCheckConstraint("ck_notifications_attempts", "attempts BETWEEN 0 AND 3"));
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Subject).HasMaxLength(Notification.SubjectMaxLength).IsRequired();
        builder.Property(n => n.Body).HasColumnType("text").IsRequired();
        builder.Property(n => n.Attempts).HasColumnType("smallint");
        builder.Property(n => n.LastError).HasMaxLength(Notification.LastErrorMaxLength);
        builder.Ignore(n => n.UpdatedAt);

        builder.HasOne<Appointment>().WithMany().HasForeignKey(n => n.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.AppointmentId, n.RecipientUserId })
            .IsUnique()
            .HasFilter("type = 'AppointmentReminder'")
            .HasDatabaseName(Constraints.NotificationsReminder);
        builder.HasIndex(n => new { n.Status, n.NextAttemptAt })
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ix_notifications_dispatch");
        builder.HasIndex(n => new { n.RecipientUserId, n.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_notifications_recipient");
    }
}
