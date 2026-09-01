using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("ratings");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Score)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasIndex(r => new { r.UserId, r.LocationId })
            .IsUnique();
    }
}
