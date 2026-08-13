# Data Sources, Attribution, and Licensing Status

This repository ships derived data from the sources below. Items marked
**⚠ UNRESOLVED** must be settled before the repository or the deployed site is
made public — see RELEASE-CHECKLIST.md.

## TxDOT CRIS crash records
- Where: `MapSandBox/wwwroot/cris-data/*`, produced by `CrisDataProcessor`
- Source: TxDOT Crash Records Information System (CRIS) public extract
- Contains crash-level records only (no person names or demographics)
- **⚠ UNRESOLVED:** confirm the CRIS extract-request terms permit
  redistribution of raw crash records in a public repository
- Note: federal law (23 U.S.C. §409) limits use of certain crash data in
  litigation; consider a data README note for downstream users

## County appraisal district (Parker CAD) parcels
- Where: `MapSandBox/wwwroot/*-parcels-with-trips.geojson`, inputs to `TripGenProcessor`
- Source: Parker County Appraisal District / TxGIO
- Owner names and mailing addresses have been **removed** from all shipped
  files (Texas Tax Code §25.025 restricts publishing addresses of protected
  persons). Do not reintroduce owner fields into public outputs.

## ITE trip-generation rates
- Where: `TripGenProcessor/Models/IteRateModels.cs` (hardcoded table), and the
  `daily_trips`/`am_peak_trips`/`pm_peak_trips` values in shipped parcel files
- Source: ITE Trip Generation Manual, 10th/11th Edition — **licensed
  commercial IP**
- **⚠ UNRESOLVED (blocking):** republishing the rate table in an open
  repository is a redistribution question only the ITE license can answer.
  Options: remove the table and require user-supplied rates, obtain
  permission, or replace with an open alternative.

## TxDOT Roadway Inventory / roads
- Where: attributes blended into `parker-roads-with-traffic*.geojson`
- Geometry base: U.S. Census TIGER/Line (public domain)
- **⚠ UNRESOLVED (blocking):** internal notes flagged the TxDOT roads dataset
  as **non-commercial license**. Determine which shipped attributes derive from
  it and whether those terms are compatible with this repository's visibility
  and any commercial use.

## TxDOT TCDS traffic counts
- Where: `TCDS.Importer` outputs, `MapSandBox/wwwroot/tiles/traffic-counts/*`
- Source: TxDOT Traffic Count Database System (hosted by MS2)
- **⚠ UNRESOLVED:** data was collected by scraping; MS2's terms of use and
  TxDOT's registered CSV-export channel should be reviewed before
  redistributing station data (see RELEASE-CHECKLIST.md).

## OpenStreetMap enrichment
- Where: parcel classifications produced by
  `TripGenProcessor/scripts/enrich_parcels_with_osm.py` (e.g. golf-course
  detection), baked into shipped parcel files
- License: ODbL. Derived data requires attribution:
  **© OpenStreetMap contributors** — this credit must appear in the app UI
  and/or data documentation wherever OSM-derived classifications are used.

## USDA SSURGO soils
- Where: `MapSandBox/wwwroot/soil-data/*`, produced by `SoilDataProcessor`
- Source: USDA NRCS Soil Survey Geographic Database — public domain; credit
  "USDA NRCS SSURGO" is customary.

## NOAA Atlas 14 rainfall
- Where: `NOAA/tx2yr05ma.asc`, `MapSandBox/wwwroot/noaa-rainfall-parker-county.geojson`
- Source: NOAA/NWS Hydrometeorological Design Studies Center — public domain.
  NOAA requests acknowledgment: "Data from NOAA/National Weather Service,
  Hydrometeorological Design Studies Center."

## TxDOT city boundaries
- Where: `MapSandBox/wwwroot/txdot-city-boundaries.geojson`
- Source: TxDOT open data; confirm current attribution string on the TxDOT
  open-data portal entry.

## Basemaps (runtime, not shipped)
- CARTO basemap styles (Voyager/Positron/Dark Matter) and OpenFreeMap Liberty
  are loaded at runtime; their attribution renders in the map control and must
  remain enabled.
