using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(Announcement.TitleMaxLength);
        builder.Property(a => a.Body).IsRequired().HasMaxLength(Announcement.BodyMaxLength);
        builder.Property(a => a.Link).HasMaxLength(Announcement.LinkMaxLength);
        builder.Property(a => a.AudienceUserIds).HasColumnType("uuid[]");
        builder.Property(a => a.CreatedAt).IsRequired();

        // The delivery worker looks for what is still being sent, oldest first; the history lists newest first.
        builder.HasIndex(a => new { a.Status, a.CreatedAt });
        builder.HasIndex(a => a.CreatedAt);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
