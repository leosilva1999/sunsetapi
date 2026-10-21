using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public const string LocationSearchVector = "SearchVector";

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

        // Backs the bounding-box prefilter of the "near me" search.
        builder.HasIndex(l => new { l.Latitude, l.Longitude });

        // Backs the text search on Name/City: a generated tsvector column + GIN index
        // (per-word prefix match, see LocationRepository). "simple" = no stemming/stop words,
        // since names are proper nouns in mixed languages.
        // Shadow property so the Domain entity stays free of Npgsql types.
        builder.Property<NpgsqlTsVector>(LocationSearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"City\", ''))", stored: true);

        builder.HasIndex(LocationSearchVector)
            .HasMethod("GIN");

        builder.HasMany(l => l.Photos)
            .WithOne(p => p.Location)
            .HasForeignKey(p => p.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Ratings)
            .WithOne(r => r.Location)
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
