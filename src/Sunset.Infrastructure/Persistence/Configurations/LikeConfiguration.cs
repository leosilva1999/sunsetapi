using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class LikeConfiguration : IEntityTypeConfiguration<Like>
{
    public void Configure(EntityTypeBuilder<Like> builder)
    {
        builder.ToTable("likes");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.HasIndex(l => new { l.UserId, l.PhotoId })
            .IsUnique();
    }
}
