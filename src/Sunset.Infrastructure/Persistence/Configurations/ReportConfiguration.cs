using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Details)
            .HasMaxLength(500);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // One report per (reporter, target) pair - mirrors the Rating/Like uniqueness pattern,
        // stops a single user from flooding the queue with repeat reports on the same content.
        builder.HasIndex(r => new { r.ReporterId, r.TargetType, r.TargetId })
            .IsUnique();

        builder.HasIndex(r => r.Status);

        // TargetId intentionally has no FK - it's a polymorphic reference (Photo or Comment
        // depending on TargetType) that no single foreign key could enforce. Existence is
        // validated in ModerationService before the Report is created.
        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
