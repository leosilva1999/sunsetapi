using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class NotificationSettingConfiguration : IEntityTypeConfiguration<NotificationSetting>
{
    public void Configure(EntityTypeBuilder<NotificationSetting> builder)
    {
        builder.ToTable("notification_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PushEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.MutedTypes).IsRequired().HasDefaultValue(0);
        builder.Property(s => s.PromotionalEnabled).IsRequired().HasDefaultValue(false);

        // One row per person.
        builder.HasIndex(s => s.UserId).IsUnique();

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
