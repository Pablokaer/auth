using CentralLogin.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralLogin.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("ApplicationUsers");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .ValueGeneratedNever();

        // Store the KeycloakSubject value object as its string value.
        builder.Property(u => u.KeycloakSubject)
            .HasConversion(
                subject => subject.Value,
                value => KeycloakSubject.Create(value))
            .HasColumnName("KeycloakSubject")
            .HasMaxLength(ApplicationUserLimits.KeycloakSubjectMaxLength)
            .IsRequired();

        // Unique index on the permanent external identity key.
        builder.HasIndex(u => u.KeycloakSubject)
            .IsUnique()
            .HasDatabaseName("IX_ApplicationUsers_KeycloakSubject");

        builder.Property(u => u.DisplayName)
            .HasMaxLength(ApplicationUserLimits.DisplayNameMaxLength);

        builder.Property(u => u.EmailSnapshot)
            .HasMaxLength(ApplicationUserLimits.EmailSnapshotMaxLength);

        builder.Property(u => u.CreatedAtUtc)
            .IsRequired();

        builder.Property(u => u.LastLoginAtUtc)
            .IsRequired();
    }
}
