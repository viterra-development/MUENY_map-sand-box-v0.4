# MUENY map-sand-box

Municipal intelligence mapping platform for Parker County, Texas. A Blazor
WebAssembly app (MapLibre GL JS + deck.gl) visualizes crash risk, traffic
volumes, parcel-level trip generation, soil characteristics, and city
boundaries, backed by a set of .NET data-processing pipelines.

## Repository layout

| Project | What it does |
|---|---|
| `MapSandBox/` | Blazor WASM web app (the map UI); static data in `wwwroot/` |
| `MapSandBox.Shared/` | Shared road-geometry/GeoJSON models and services |
| `CrisDataProcessor/` | TxDOT CRIS crash CSV exports → crash/risk GeoJSON layers |
| `TripGenProcessor/` | CAD parcels → land-use classification → trip generation → road stress |
| `TCDS.Importer/` | TxDOT traffic-count collection + AADT estimation (Phase 1) |
| `SoilDataProcessor/` | USDA SSURGO soil data → clay/ksat GeoJSON |
| `NoaaDataProcessor/` | NOAA Atlas 14 rainfall grids → GeoJSON |
| `TileUploadUtility/` | Bulk-uploads generated tiles to Azure blob storage |
| `src/worker.js` | Cloudflare Worker: serves the built app + `/api/log-visit` |
| `Documentation/` | Pipeline docs (`DATA_PROCESSING_README.md`) and plan history |

## Build and run

Prerequisites: .NET SDK 9+ (builds under 10.x).

```bash
dotnet build MapSandBox.sln
dotnet run --project MapSandBox
```

The web app runs out of the box against the GeoJSON files committed in
`MapSandBox/wwwroot/`.

The data processors need external inputs that are **not** in the repository
(CRIS CSV exports, DEM rasters, CAD parcel source files) — see each project's
`appsettings.json` and `Documentation/DATA_PROCESSING_README.md`.

## Deployment

- **Cloudflare Workers** (`wrangler.toml`, `.github/workflows/deploy-cloudflare.yml`):
  publishes the Blazor build with `src/worker.js` in front (security headers +
  visit logging).
- An Azure Static Web Apps workflow also exists; pick one target and retire the
  other (see RELEASE-CHECKLIST.md).

## Data sources and licensing

Shipped map layers derive from TxDOT CRIS extracts, TIGER/Line, county CAD
parcel data (owner PII stripped), USDA SSURGO, NOAA Atlas 14, OpenStreetMap
enrichment, and TxDOT traffic counts. See **DATA-ATTRIBUTION.md** for details
and open licensing questions that must be resolved before redistribution.

**License:** not yet chosen — all rights reserved until a LICENSE file is
added (tracked in RELEASE-CHECKLIST.md).

## Known data caveats

- The CRIS severity-code mapping was corrected in Aug 2026 (fatal was
  previously mislabeled). The GeoJSON files under `MapSandBox/wwwroot/cris-data/`
  **must be regenerated** with the fixed CrisDataProcessor before any public
  deployment; until then their severity labels and derived risk scores are wrong.
- Parcel classifications marked `Heuristic` in the `notes` property are
  stopgap estimates, not authoritative land-use determinations.
