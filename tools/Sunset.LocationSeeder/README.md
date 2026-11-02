# Sunset.LocationSeeder

Registers a curated list of locations (`data/launch-locations.json`) through the application's own
`LocationService`, so slugs, the city/locality link and the duplicate check behave exactly as they do for a user.
Run by hand; the API never calls it.

```
# simulation: does everything inside a transaction and rolls it back
dotnet run --project tools/Sunset.LocationSeeder -- --created-by <e-mail of the owning account>

# write
dotnet run --project tools/Sunset.LocationSeeder -- --created-by <e-mail> --apply
```

The connection string comes from `--connection` or `ConnectionStrings__DefaultConnection`.

- **Safe to re-run**: a place that already exists is reported ("already registered") and skipped, never duplicated.
- **Checks before writing**: the municipality (and the locality, when given) must exist in the catalog, the locality must
  belong to that municipality, and the point must be within 80 km of the municipality's centre (10 km of the locality's).
  Any problem aborts the whole run with nothing written.
- **Owner**: `--created-by` is the account stored as `CreatedByUserId` (visible to moderation only).
- **Cache**: it runs outside the API, so the API's 60 s response cache and the 1 h sitemap cache catch up on their own.

## The list

`data/launch-locations.json`: name, category, coordinates, municipality slug, optional locality slug and a short
description written for Sunset. The `references` array lists the guides and rankings the selection was drawn from;
coordinates come from OpenStreetMap (Nominatim). A few points are approximate (the centre of a waterfront or a trailhead
rather than the exact viewing spot: Orla de Santo Antônio de Lisboa, Pedra Furada, Ponta do Cururu, Ponte JK) and can be
corrected by moderation (`PATCH /moderation/locations/{id}/coordinates`).

To add places, append to the file and run the simulation first.
