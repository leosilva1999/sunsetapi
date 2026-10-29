using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class LegalAcceptanceConfiguration : IEntityTypeConfiguration<LegalAcceptance>
{
    public void Configure(EntityTypeBuilder<LegalAcceptance> builder)
    {
        builder.ToTable("legal_acceptances");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // One row per (user, document, version); also serves the "all acceptances of a user" lookup.
        builder.HasIndex(a => new { a.UserId, a.DocumentType, a.Version })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
