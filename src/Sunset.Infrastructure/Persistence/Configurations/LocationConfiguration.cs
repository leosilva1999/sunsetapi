using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(l => l.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.Latitude)
            .IsRequired();

        builder.Property(l => l.Longitude)
            .IsRequired();

        builder.Property(l => l.AvgRating)
            .IsRequired()
            .HasPrecision(3, 2);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.HasIndex(l => l.City);

        builder.HasMany(l => l.Photos)
            .WithOne(p => p.Location)
            .HasForeignKey(p => p.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Ratings)
            .WithOne(r => r.Location)
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
