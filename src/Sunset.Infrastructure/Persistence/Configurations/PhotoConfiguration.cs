using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("photos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.ImageUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(p => p.Caption)
            .HasMaxLength(500);

        builder.Property(p => p.LikesCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.CommentsCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.LocationId);
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.DeletedAt);

        builder.HasMany(p => p.Likes)
            .WithOne(l => l.Photo)
            .HasForeignKey(l => l.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Comments)
            .WithOne(c => c.Photo)
            .HasForeignKey(c => c.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Who moderated it away, if anyone - Restrict like every other User FK (see
        // UserConfiguration), so a moderator's account can't be deleted out from under this.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
