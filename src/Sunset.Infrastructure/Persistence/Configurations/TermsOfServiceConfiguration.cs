using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class TermsOfServiceConfiguration : IEntityTypeConfiguration<TermsOfService>
{
    public void Configure(EntityTypeBuilder<TermsOfService> builder)
    {
        builder.ToTable("terms_of_service");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Content)
            .IsRequired()
            .HasColumnType("mediumtext");

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.HasIndex(t => t.Version)
            .IsUnique();

        builder.HasOne(t => t.UpdatedBy)
            .WithMany()
            .HasForeignKey(t => t.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
