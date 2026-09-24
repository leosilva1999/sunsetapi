using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Content)
            .IsRequired()
            .HasColumnType("mediumtext");

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        // Versions are numbered independently per document type (terms of service and privacy
        // policy each start their own sequence at 1), so the unique constraint is on the pair.
        builder.HasIndex(t => new { t.DocumentType, t.Version })
            .IsUnique();

        builder.HasOne(t => t.UpdatedBy)
            .WithMany()
            .HasForeignKey(t => t.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
