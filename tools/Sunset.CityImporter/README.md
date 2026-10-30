# Sunset.CityImporter

Loads the `cities` table (municipalities and localities) from a [GeoNames](https://www.geonames.org/) country
dump. It is a command-line tool, **not** part of the API: the API never downloads anything at run time.

## Running it

```bash
# 1. get the data (fora do repositório)
curl -O https://download.geonames.org/export/dump/BR.zip

# 2. rehearsal: shows what would be written, touches no database
dotnet run --project tools/Sunset.CityImporter -- --country BR --file BR.zip --dry-run

# 3. the real thing (the migration AddCities must already be applied)
dotnet run --project tools/Sunset.CityImporter -- --country BR --file BR.zip \
    --connection "Host=...;Database=sunset;Username=...;Password=...;SSL Mode=Require"
# or: set ConnectionStrings__DefaultConnection instead of --connection
```

Expected for Brazil (2026-10-29 dump): **5.571 municipalities** (5.570 from GeoNames + 1 added by hand) and
**about 59.8 thousand localities**; about 37 s on Neon for the first load. Take a database backup first in production.

## What it does

- **Municipality** = GeoNames `ADM2`. Its `admin2` code *is* the 7-digit IBGE code (`NationalCode`).
- **Locality** = a populated place (`PPL`, `PPLL`, `PPLA`…`PPLC`; **not** `PPLX`, which are sections/neighbourhoods) whose
  `admin2` points to an imported municipality. Skipped: places with no municipality (~1.1k) and the municipality's
  own seat, which GeoNames repeats as a locality with the same name (~5.9k) — the municipality's page covers it.
- **Name**: the IBGE spelling wins for Brazilian municipalities (`data/city-name-overrides.json`, keyed by IBGE code;
  the GeoNames spelling is kept in `SourceName` and stays searchable). A person should review that file.
- **Missing from the dataset**: `data/city-additions.json` adds municipalities by hand (today: Boa Esperança do Norte,
  MT — IBGE code confirmed against the IBGE API, coordinates/population from Wikidata `Q123021065`). When GeoNames
  starts carrying one, the existing row is *adopted* (same row, same slug).
- **Idempotent**: matched by `(source, geonameid)`; running again changes nothing. Updates only name, coordinates,
  time zone, population and parent.
- **Slugs never change** once stored (they are in public URLs): `imbituba-sc`, `praia-do-rosa-imbituba-sc`; a clash
  inside one municipality gets `-2`, `-3`…
- **Never deletes**: a record that left the dataset becomes `IsActive = false` (and comes back if it reappears).

## Attribution (required)

GeoNames data is licensed **CC BY 4.0**: wherever it is shown, credit "Dados geográficos: GeoNames (CC BY 4.0)" with a
link to https://www.geonames.org/.

## Other countries

`CityImportRules` (in `Sunset.Application/Cities/Import`) holds the rules per country — which GeoNames feature codes are
"municipalities" and "localities", and the state-code table. Only Brazil exists; adding a country means adding its
rules (the definition of "city" differs from country to country) and deciding its slug policy.
