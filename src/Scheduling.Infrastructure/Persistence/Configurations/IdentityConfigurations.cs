using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Name).HasMaxLength(ValidationLimits.NameMaxLength).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(ValidationLimits.EmailMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(ValidationLimits.PasswordHashMaxLength).IsRequired();
        builder.Property(u => u.Role).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName(Constraints.UsersEmail);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(ValidationLimits.NameMaxLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(ValidationLimits.EmailMaxLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(ValidationLimits.PhoneMaxLength).IsRequired();
        builder.HasOne<User>().WithOne().HasForeignKey<Customer>(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("ux_customers_user_id");
    }
}
