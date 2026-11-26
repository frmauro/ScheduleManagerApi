using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("providers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DisplayName).HasMaxLength(ValidationLimits.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(ValidationLimits.ProviderDescriptionMaxLength);
        builder.Property(p => p.Category).IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(ValidationLimits.EmailMaxLength).IsRequired();
        builder.Property(p => p.ContactPhone).HasMaxLength(ValidationLimits.PhoneMaxLength).IsRequired();
        builder.Property(p => p.TimeZoneId).HasMaxLength(ValidationLimits.TimeZoneIdMaxLength).IsRequired();

        builder.OwnsOne(p => p.Address, address =>
        {
            address.Property(a => a.Street).HasMaxLength(ValidationLimits.StreetMaxLength).IsRequired();
            address.Property(a => a.Number).HasMaxLength(ValidationLimits.NumberMaxLength).IsRequired();
            address.Property(a => a.Complement).HasMaxLength(ValidationLimits.ComplementMaxLength);
            address.Property(a => a.District).HasMaxLength(ValidationLimits.DistrictMaxLength).IsRequired();
            address.Property(a => a.City).HasMaxLength(ValidationLimits.CityMaxLength).IsRequired();
            address.Property(a => a.State).HasMaxLength(ValidationLimits.StateLength).IsFixedLength().IsRequired();
            address.Property(a => a.PostalCode).HasMaxLength(ValidationLimits.PostalCodeLength).IsFixedLength().IsRequired();
            address.HasIndex(a => a.City).HasDatabaseName("ix_providers_address_city");
        });
        builder.Navigation(p => p.Address).IsRequired();

        builder.OwnsOne(p => p.BookingPolicy, policy =>
        {
            policy.Property(b => b.AutoConfirm).HasColumnName("AutoConfirm");
            policy.Property(b => b.SlotStepMinutes).HasColumnName("SlotStepMinutes").HasColumnType("smallint");
            policy.Property(b => b.BufferMinutes).HasColumnName("BufferMinutes").HasColumnType("smallint");
            policy.Property(b => b.MinBookingNoticeMinutes).HasColumnName("MinBookingNoticeMinutes");
            policy.Property(b => b.BookingHorizonDays).HasColumnName("BookingHorizonDays").HasColumnType("smallint");
            policy.Property(b => b.MinCancellationNoticeMinutes).HasColumnName("MinCancellationNoticeMinutes");
        });
        builder.Navigation(p => p.BookingPolicy).IsRequired();

        builder.HasOne<User>().WithOne().HasForeignKey<Provider>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ux_providers_user_id");
        builder.HasIndex(p => new { p.Category, p.IsActive }).HasDatabaseName("ix_providers_category_active");
        builder.HasIndex(p => p.DisplayName).HasDatabaseName("ix_providers_display_name");

        builder.HasMany(p => p.WorkingHours).WithOne().HasForeignKey(w => w.ProviderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.WorkingHours).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Exceptions).WithOne().HasForeignKey(e => e.ProviderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("provider_working_hours", t =>
            t.HasCheckConstraint("ck_working_hours_range", "start_time < end_time AND day_of_week BETWEEN 0 AND 6"));
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.DayOfWeek).HasConversion<short>().HasColumnType("smallint");
        builder.Property(w => w.StartTime).HasColumnType("time");
        builder.Property(w => w.EndTime).HasColumnType("time");
        builder.Ignore(w => w.Range);
        builder.HasIndex(w => new { w.ProviderId, w.DayOfWeek }).HasDatabaseName("ix_working_hours_provider_day");
    }
}

internal sealed class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
        builder.ToTable("provider_schedule_exceptions", t =>
        {
            t.HasCheckConstraint("ck_schedule_exceptions_dates", "end_date >= start_date");
            t.HasCheckConstraint("ck_schedule_exceptions_times", "(start_time IS NULL AND end_time IS NULL) OR (start_time < end_time)");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.StartDate).HasColumnType("date");
        builder.Property(e => e.EndDate).HasColumnType("date");
        builder.Property(e => e.StartTime).HasColumnType("time");
        builder.Property(e => e.EndTime).HasColumnType("time");
        builder.Property(e => e.Reason).HasMaxLength(ValidationLimits.ExceptionReasonMaxLength);
        builder.Ignore(e => e.Range);
        builder.Ignore(e => e.IsFullDay);
        builder.HasIndex(e => new { e.ProviderId, e.StartDate, e.EndDate }).HasDatabaseName("ix_exceptions_provider_dates");
    }
}

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", t =>
        {
            t.HasCheckConstraint("ck_services_duration", "duration_minutes BETWEEN 5 AND 720 AND duration_minutes % 5 = 0");
            t.HasCheckConstraint("ck_services_price", "price >= 0 AND price <= 99999.99");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(ValidationLimits.ServiceNameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(ValidationLimits.ServiceDescriptionMaxLength);
        builder.Property(s => s.DurationMinutes).HasColumnType("smallint");
        builder.Property(s => s.Price).HasColumnType("numeric(10,2)");
        builder.HasOne<Provider>().WithMany().HasForeignKey(s => s.ProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.ProviderId).HasDatabaseName("ix_services_provider");

        // ux_services_provider_name (provider_id, lower(name)) é criado via SQL na migration (índice de expressão).
    }
}
