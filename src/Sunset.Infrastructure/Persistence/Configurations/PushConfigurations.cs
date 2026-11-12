using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("push_devices");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Token).IsRequired().HasMaxLength(255);
        builder.Property(d => d.AppVersion).HasMaxLength(32);
        builder.Property(d => d.Locale).HasMaxLength(32);
        builder.Property(d => d.LastSeenAt).IsRequired();

        // One owner per token.
        builder.HasIndex(d => d.Token).IsUnique();
        builder.HasIndex(d => new { d.UserId, d.LastSeenAt });
        builder.HasIndex(d => new { d.UserId, d.InstallId });
        // Retention clean-up scans by last-seen date.
        builder.HasIndex(d => d.LastSeenAt);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PushTicketConfiguration : IEntityTypeConfiguration<PushTicket>
{
    public void Configure(EntityTypeBuilder<PushTicket> builder)
    {
        builder.ToTable("push_tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.ProviderTicketId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Token).IsRequired().HasMaxLength(255);
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => t.CreatedAt);
    }
}
