# TripGenProcessor

ITE-based trip generation pipeline for Parker County / Willow Park, TX.  
Follows the same pattern as `TCDS.Importer` in the map-sand-box project.

## Quick Start

```bash
cd TripGenProcessor
dotnet restore
dotnet run
```

## Architecture

```
TripGenProcessor/
├── Program.cs                  # Pipeline orchestrator (6 steps)
├── Models/
│   ├── IteRateModels.cs        # ITE rate lookup table (20 land-use codes)
│   ├── ParcelModels.cs         # CadParcel + RoadSegment models
│   └── TripGenerationResult.cs # Output models + summary
├── Services/
│   ├── ParcelDataLoader.cs     # Loads GeoJSON (parcels, roads, boundaries)
│   ├── LandUseClassifier.cs    # state_cd → ITE code mapping
│   ├── TripCalculator.cs       # Core formula: daily_trips = rate × units
│   ├── RoadLinker.cs           # R-tree spatial index, nearest-segment snap
│   ├── TripAggregator.cs       # Sum trips per road + V/C ratio
│   └── OutputWriter.cs         # GeoJSON + JSON output
├── data/                       # Input files (put CAD parcels here)
├── output/                     # Generated trip data
└── appsettings.json            # Configuration
```

## Pipeline Steps

1. **Load Data** — Parse parcel + road GeoJSON with flexible attribute mapping
2. **Clip to City** — Filter parcels within Willow Park boundary
3. **Calculate Trips** — Classify land use → look up ITE rate → multiply by units
4. **Link to Roads** — Snap each parcel centroid to nearest TIGER road segment
5. **Aggregate** — Sum parcel trips onto road segments, compute V/C ratios
6. **Write Output** — Enriched GeoJSON files + JSON summary report

## Core Formula

```
daily_trips = ite_rate[ite_code] × units
am_peak     = daily_trips × am_k_factor
pm_peak     = daily_trips × pm_k_factor
```

## Input Data Required

| File | Description | Source |
|------|-------------|--------|
| `county-cad-parcels-willow-park.geojson` | Parcels with `state_cd`, `legal_acreage`, `imprv_sqft` | Parker County CAD / TxGIO |
| `parker-roads-with-traffic.geojson` | TIGER roads with AADT | Already in repo |
| `txdot-city-boundaries-filtered.geojson` | City boundary for clipping | Already in repo |

### Where to get Parker County Parcels

- **TxGIO DataHub** (free): https://tnris.org/stratmap/land-parcels.html
- **Texas County GIS Data** (premium): https://texascountygisdata.com/product/parker-county-shapefile-and-property-data/
- **Koordinates**: https://koordinates.com/layer/10039-parker-county-texas-parcels/
- **Parker County CAD**: https://iswdataclient.azurewebsites.net/webindex.aspx?dbkey=parkercad

## Output Files

- `willow-park-parcels-with-trips.geojson` — Each parcel with `daily_trips`, `am_peak_trips`, `pm_peak_trips`, `ite_code`, `access_road`
- `parker-roads-with-trip-volumes.geojson` — Road segments with `trip_daily_total`, `vc_ratio`
- `trip-generation-summary.json` — Statistics, category breakdown, top roads, warnings

## Configuration (appsettings.json)

| Key | Default | Description |
|-----|---------|-------------|
| `MaxSnapDistanceMeters` | 500 | Max distance to link parcel to road |
| `DirectionalSplit` | 0.50 | In/out directional split |
| `DefaultDwellingUnitsPerParcel` | 1 | DU for single-family without explicit count |
| `DefaultSqftPerAcreFactor` | 10000 | Fallback sqft estimate |

## Integration with map-sand-box

Drop the processor into the solution alongside TCDS.Importer:

```
map-sand-box-main/
├── TCDS.Importer/          # Existing traffic count processor
├── CrisDataProcessor/      # Existing crash data processor
├── TripGenProcessor/       # ← New: this project
└── MapSandbox.Client/      # Blazor WASM frontend
```

Add to solution: `dotnet sln add TripGenProcessor/TripGenProcessor.csproj`
