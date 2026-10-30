using System.Text.Json.Serialization;

namespace Sunset.Application.DTOs.Sitemap;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SitemapEntryType
{
    City,
    Location,
    Photo
}

/// <param name="Slug">The public URL key; null for photos (they are addressed by id).</param>
/// <param name="LastModified">UTC. City: the latest change among its locations. Location: its creation, its latest
/// visible photo or its latest rating. Photo: its creation.</param>
public sealed record SitemapEntryResponse(Guid Id, string? Slug, DateTime LastModified);
