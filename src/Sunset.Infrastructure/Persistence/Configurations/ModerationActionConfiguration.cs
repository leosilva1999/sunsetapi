using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class ModerationActionConfiguration : IEntityTypeConfiguration<ModerationAction>
{
    public void Configure(EntityTypeBuilder<ModerationAction> builder)
    {
        builder.ToTable("moderation_actions");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.TargetDescription)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.Notes)
            .HasMaxLength(1000);

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.HasIndex(m => m.CreatedAt);

        builder.HasOne(m => m.Moderator)
            .WithMany()
            .HasForeignKey(m => m.ModeratorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
