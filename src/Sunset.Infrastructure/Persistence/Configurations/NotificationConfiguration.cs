using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.TargetDescription)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.HasIndex(n => n.RecipientUserId);

        builder.HasOne(n => n.Announcement)
            .WithMany()
            .HasForeignKey(n => n.AnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);

        // A person gets a given announcement once: also the safety net if a delivery batch is retried.
        builder.HasIndex(n => new { n.RecipientUserId, n.AnnouncementId })
            .IsUnique()
            .HasFilter("\"AnnouncementId\" IS NOT NULL");
        builder.HasIndex(n => n.AnnouncementId);

        builder.HasOne(n => n.Recipient)
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Actor)
            .WithMany()
            .HasForeignKey(n => n.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
