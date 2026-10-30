using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Configurations;

public class CityConfiguration : IEntityTypeConfiguration<City>
{
    public const string CitySearchVector = "SearchVector";

    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Kind).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.SourceName).IsRequired().HasMaxLength(150);
        builder.Property(c => c.AlternateNames).HasColumnType("text");
        builder.Property(c => c.SearchText).IsRequired().HasColumnType("text");
        builder.Property(c => c.Country).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.AdminArea).IsRequired().HasMaxLength(10);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(160);
        builder.Property(c => c.Source).IsRequired().HasMaxLength(20);
        builder.Property(c => c.NationalCode).HasMaxLength(20);
        builder.Property(c => c.TimeZoneId).HasMaxLength(64);
        builder.Property(c => c.CreatedAt).IsRequired();

        // The public URL key: unique, and never rewritten once stored.
        builder.HasIndex(c => c.Slug).IsUnique();

        // One row per record of a source. NULL ExternalId (manual additions) never collides in Postgres.
        builder.HasIndex(c => new { c.Source, c.ExternalId }).IsUnique();

        builder.HasIndex(c => new { c.Country, c.AdminArea, c.Name });
        builder.HasIndex(c => c.ParentCityId);
        builder.HasIndex(c => c.NationalCode);

        // Backs the most-populous-first order of the autocomplete.
        builder.HasIndex(c => new { c.IsActive, c.Kind, c.Population });

        // Prefix search over the accent-free SearchText ("brasil" finds Brasília; alternate names count).
        // "simple" = no stemming: these are proper nouns. Shadow property keeps Npgsql out of the Domain.
        builder.Property<NpgsqlTsVector>(CitySearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql("to_tsvector('simple', \"SearchText\")", stored: true);

        builder.HasIndex(CitySearchVector).HasMethod("GIN");

        builder.HasOne(c => c.ParentCity)
            .WithMany()
            .HasForeignKey(c => c.ParentCityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
