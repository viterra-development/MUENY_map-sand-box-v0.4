#!/usr/bin/env python3
"""
Enrich parcel GeoJSON with OpenStreetMap POI classifications.

For each parcel, look up whether any OSM commercial/institutional POI sits within
POI_JOIN_RADIUS_M meters of the parcel centroid. If yes, populate the parcel's
`state_cd` with the equivalent Parker CAD code (F1 for commercial, F2 for
industrial, X3/X4 for church/school, etc.). TripGenProcessor's existing
state_cd → ITE lookup then does canonical classification instead of the
heuristic ladder.

Usage:
    python3 enrich_parcels_with_osm.py \\
        --parcels ~/muenytx-work/parker-county-parcels.geojson \\
        --output ~/muenytx-work/parker-county-parcels-osm.geojson

Caches the Overpass response at ~/muenytx-work/parker-osm-poi-cache.geojson
so subsequent runs are fast (no API call).
"""
import argparse
import json
import os
import sys
import time
from pathlib import Path

import geopandas as gpd
import pandas as pd
import requests
from shapely.geometry import Point, shape

# Parker County, TX approximate bounding box (padded slightly)
PARKER_BBOX = (-98.20, 32.53, -97.42, 33.05)   # (west, south, east, north)
POI_JOIN_RADIUS_M = 25                          # spatial-join buffer

# OSM tag → Parker CAD state_cd. Chosen so TripGenProcessor's canonical
# IteRateLookup.CadToIte hits the right ITE rate directly.
#   F1 → 820 (Commercial retail)
#   F2 → 110 (Industrial)
#   B1 → 220 (Apartments)
#   X3 → 560 (Church)
#   X4 → 520/522/530 (School — refined by name/level inside classifier)
#   X1 → 710 (Government / office / hospital)
OSM_TO_STATE_CD = [
    # (predicate: tags dict → bool, state_cd, label)
    (lambda t: t.get('leisure') == 'golf_course'
            or t.get('golf') == 'course',                              'GOLF', 'Golf Course (OSM)'),
    (lambda t: t.get('amenity') == 'place_of_worship',                 'X3', 'Church (OSM)'),
    (lambda t: t.get('amenity') in ('school', 'kindergarten',
                                     'college', 'university'),          'X4', 'School (OSM)'),
    (lambda t: t.get('amenity') in ('hospital', 'clinic', 'doctors',
                                     'dentist', 'pharmacy'),            'X1', 'Health (OSM)'),
    (lambda t: t.get('amenity') in ('fire_station', 'police', 'library',
                                     'townhall', 'courthouse',
                                     'post_office', 'community_centre'),'X1', 'Government (OSM)'),
    (lambda t: t.get('landuse') == 'industrial'
            or t.get('industrial'),                                     'F2', 'Industrial (OSM)'),
    (lambda t: t.get('building') in ('apartments',
                                      'residential') and
                (t.get('building:levels') or '0').isdigit() and
                int(t.get('building:levels') or '0') > 1,               'B1', 'Multifamily (OSM)'),
    # Anything with a shop=* tag → retail
    (lambda t: bool(t.get('shop')),                                     'F1', 'Retail (OSM shop=*)'),
    # Restaurants / bars / cafes
    (lambda t: t.get('amenity') in ('restaurant', 'fast_food', 'cafe',
                                     'bar', 'pub', 'food_court',
                                     'ice_cream'),                      'F1', 'Restaurant (OSM)'),
    # Fuel + car wash + retail-like
    (lambda t: t.get('amenity') in ('fuel', 'car_wash', 'bank', 'atm',
                                     'marketplace'),                    'F1', 'Retail (OSM amenity)'),
    # Office / veterinary / any tag with office=*
    (lambda t: bool(t.get('office'))
            or t.get('amenity') in ('veterinary', 'coworking_space'),   'F1', 'Office (OSM)'),
    # Fitness / entertainment / lodging
    (lambda t: t.get('leisure') in ('fitness_centre', 'sports_centre',
                                     'bowling_alley')
            or t.get('tourism') in ('hotel', 'motel', 'guest_house',
                                     'hostel'),                          'F1', 'Commercial (OSM leisure/tourism)'),
]


def classify_tags(tags):
    """Return (state_cd, label) or (None, None)."""
    for pred, code, label in OSM_TO_STATE_CD:
        if pred(tags):
            return code, label
    return None, None


def fetch_overpass_pois(bbox, cache_path):
    """Query Overpass for Parker County POIs. Cache to disk."""
    if cache_path.exists():
        print(f"[osm] Using cached POI file: {cache_path}")
        return gpd.read_file(cache_path)

    print(f"[osm] Querying Overpass API for POIs in Parker County bbox …")
    w, s, e, n = bbox
    query = f"""
    [out:json][timeout:120];
    (
      node["shop"]({s},{w},{n},{e});
      way["shop"]({s},{w},{n},{e});
      node["amenity"]({s},{w},{n},{e});
      way["amenity"]({s},{w},{n},{e});
      node["office"]({s},{w},{n},{e});
      way["office"]({s},{w},{n},{e});
      way["landuse"="industrial"]({s},{w},{n},{e});
      way["industrial"]({s},{w},{n},{e});
      way["building"~"apartments|residential"]({s},{w},{n},{e});
      node["leisure"~"fitness_centre|sports_centre|bowling_alley|golf_course"]({s},{w},{n},{e});
      way["leisure"~"fitness_centre|sports_centre|bowling_alley|golf_course"]({s},{w},{n},{e});
      relation["leisure"="golf_course"]({s},{w},{n},{e});
      node["tourism"~"hotel|motel|guest_house|hostel"]({s},{w},{n},{e});
      way["tourism"~"hotel|motel|guest_house|hostel"]({s},{w},{n},{e});
    );
    out center tags;
    """
    endpoints = [
        'https://overpass-api.de/api/interpreter',
        'https://overpass.kumi.systems/api/interpreter',
        'https://overpass.private.coffee/api/interpreter',
    ]
    resp = None
    last_err = None
    for url in endpoints:
        try:
            resp = requests.post(
                url, data={'data': query}, timeout=180,
                headers={'User-Agent': 'MUENY-TripGenProcessor/1.0 (spencer@hodgetx.com)'},
            )
            resp.raise_for_status()
            print(f"[osm] {url} → HTTP {resp.status_code}")
            break
        except Exception as e:
            print(f"[osm] {url} failed: {e}")
            last_err = e
            resp = None
    if resp is None:
        raise SystemExit(f"All Overpass endpoints failed. Last error: {last_err}")
    data = resp.json()
    elements = data.get('elements', [])
    print(f"[osm] Received {len(elements):,} elements")

    records = []
    for el in elements:
        tags = el.get('tags', {})
        state_cd, label = classify_tags(tags)
        if not state_cd:
            continue
        # Node has lat/lon; way has center: {lat, lon}
        lat, lon = None, None
        if el.get('type') == 'node':
            lat, lon = el.get('lat'), el.get('lon')
        elif 'center' in el:
            lat, lon = el['center'].get('lat'), el['center'].get('lon')
        if lat is None or lon is None:
            continue
        records.append({
            'osm_id':   f"{el.get('type', '?')}-{el.get('id', '?')}",
            'state_cd': state_cd,
            'osm_label':label,
            'name':     tags.get('name', ''),
            'geometry': Point(lon, lat),
        })
    gdf = gpd.GeoDataFrame(records, crs='EPSG:4326')
    print(f"[osm] Classified {len(gdf):,} POIs into state_cd buckets")
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    gdf.to_file(cache_path, driver='GeoJSON')
    return gdf


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--parcels', required=True, help='Input parcel GeoJSON')
    ap.add_argument('--output',  required=True, help='Enriched output GeoJSON')
    ap.add_argument('--cache',   default=str(Path.home() / 'muenytx-work' / 'parker-osm-poi-cache.geojson'))
    ap.add_argument('--radius',  type=int, default=POI_JOIN_RADIUS_M)
    args = ap.parse_args()

    cache_path = Path(args.cache)
    pois = fetch_overpass_pois(PARKER_BBOX, cache_path)

    print(f"[parcels] Loading {args.parcels}…")
    t0 = time.time()
    parcels = gpd.read_file(args.parcels)
    print(f"[parcels] {len(parcels):,} parcels in {time.time()-t0:.1f}s")

    # Reproject both to Texas North Central meters (EPSG:2276) so we can buffer
    # by meters rather than degrees.
    print(f"[join] Reprojecting to EPSG:2276 (Texas NC ft/m)…")
    parcels_m = parcels.to_crs(2276)
    pois_m = pois.to_crs(2276)

    # Spatial join on ROW INDICES (not prop_id) because TxGIO has ~6,500 parcels
    # with prop_id="0" (rights-of-way, road slivers). Joining by prop_id would
    # broadcast one match to all 6,500 → bogus X4 count.
    #
    # Join in TWO passes:
    #  Pass 1 — POI *inside the parcel polygon*. The strongest possible signal:
    #    a hospital building node inside an 8-acre hospital campus parcel.
    #    (A centroid buffer misses these — the centroid of a big parcel can be
    #    100m+ from the building node.)
    #  Pass 2 — for parcels with no polygon hit, POI within `radius` meters of
    #    the parcel centroid. Catches small storefronts whose OSM node was
    #    dropped on the sidewalk/street outside the lot line.
    print(f"[join] Pass 1: POI within parcel polygon …")
    t0 = time.time()
    parcels_poly = gpd.GeoDataFrame(
        {'_parcel_idx': parcels_m.index},
        geometry=parcels_m.geometry,
        crs=2276,
    )
    poi_cols = pois_m[['state_cd', 'osm_label', 'name', 'geometry']].rename(columns={'name': 'osm_name'})
    joined_poly = gpd.sjoin(parcels_poly, poi_cols, how='inner', predicate='contains')
    print(f"[join] Pass 1: {len(joined_poly):,} POI-in-polygon rows in {time.time()-t0:.1f}s")

    print(f"[join] Pass 2: centroid buffer radius={args.radius}m for the rest …")
    t0 = time.time()
    hit_idx = set(joined_poly['_parcel_idx'])
    remaining = parcels_m.loc[~parcels_m.index.isin(hit_idx)]
    parcel_buffers = gpd.GeoDataFrame(
        {'_parcel_idx': remaining.index},
        geometry=remaining.geometry.centroid.buffer(args.radius * 3.28084),  # meters → survey feet
        crs=2276,
    )
    joined_buf = gpd.sjoin(parcel_buffers, poi_cols, how='inner', predicate='intersects')
    print(f"[join] Pass 2: {len(joined_buf):,} centroid-buffer rows in {time.time()-t0:.1f}s")

    joined = pd.concat([joined_poly, joined_buf], ignore_index=True)
    print(f"[join] Combined: {len(joined):,} parcel×POI rows")

    # Priority: GOLF > X4 > X3 > F2 > F1 > X1 > B1 — a golf course polygon often
    # contains a clubhouse restaurant/pro-shop POI; the course wins.
    priority = {'GOLF': 7, 'X4': 6, 'X3': 5, 'F2': 4, 'F1': 3, 'X1': 2, 'B1': 1}
    joined['pri'] = joined['state_cd'].map(priority).fillna(0)
    top = (joined.sort_values(['_parcel_idx', 'pri'], ascending=[True, False])
                 .drop_duplicates(subset='_parcel_idx', keep='first')
                 .set_index('_parcel_idx')[['state_cd', 'osm_label', 'osm_name']])

    # Guardrails — drop matches that would misrepresent the parcel:
    #  1. Government-owned parcels (state parks, city complexes) keep their owner-based
    #     classification. A camp store inside a 160-acre state park must not turn the
    #     whole tract into ITE-820 retail (LAKE MINERAL WELLS SP was generating 65K
    #     daily trips from exactly this).
    #  2. Commercial tags (F1/F2/B1) on parcels > 40 acres are rejected — one POI node
    #     is not representative of a tract that size. Institutional campuses (X1/X3/X4:
    #     schools, churches, hospitals) may legitimately be large, so those pass.
    GOV_OWNER_MARKERS = ('CITY OF', 'COUNTY OF', 'STATE OF TEXAS', 'PARKS & WILDLIFE',
                         'PARKS AND WILDLIFE', ' DEPT', 'UNITED STATES', ' USA ',
                         'ISD', 'SCHOOL DIST', 'RIVER AUTHORITY', 'UTILITY DISTRICT')
    owner_series = parcels['owner_name'].fillna('').str.upper() if 'owner_name' in parcels else None
    acreage_series = parcels['legal_acreage'].fillna(0) if 'legal_acreage' in parcels else None
    dropped_gov = dropped_big = 0
    keep_rows = []
    for idx in top.index:
        code = top.loc[idx, 'state_cd']
        if owner_series is not None and idx in owner_series.index:
            o = owner_series.loc[idx]
            if any(m in o for m in GOV_OWNER_MARKERS):
                dropped_gov += 1
                continue
        if code in ('F1', 'F2', 'B1') and acreage_series is not None and idx in acreage_series.index:
            if acreage_series.loc[idx] > 40:
                dropped_big += 1
                continue
        # A golf-course polygon overlaps dozens of adjoining house lots; only
        # parcels with real acreage can actually BE the course.
        if code == 'GOLF' and acreage_series is not None and idx in acreage_series.index:
            if acreage_series.loc[idx] < 2:
                dropped_big += 1
                continue
        keep_rows.append(idx)
    top = top.loc[keep_rows]
    print(f"[guard] Dropped {dropped_gov} government-owned + {dropped_big} oversized/undersized matches")

    # Merge back by row index, not prop_id.
    parcels['_row_idx'] = parcels.index
    parcels = parcels.merge(top, left_on='_row_idx', right_index=True,
                            how='left', suffixes=('', '_osm'))
    parcels = parcels.drop(columns=['_row_idx'])

    matched = parcels['state_cd_osm'].notna() if 'state_cd_osm' in parcels else parcels['osm_label'].notna()
    # state_cd_osm may not exist if the merge used the raw column name; handle both.
    if 'state_cd_osm' in parcels.columns:
        parcels.loc[matched, 'state_cd'] = parcels.loc[matched, 'state_cd_osm']
        parcels = parcels.drop(columns=['state_cd_osm'])
    parcels['class_source'] = parcels['osm_label'].fillna('')
    parcels = parcels.drop(columns=['osm_label'])
    n_after = parcels['state_cd'].notna().sum()

    total = len(parcels)
    n_matched = int(matched.sum())
    print()
    print(f"[result] {n_matched:,} of {total:,} parcels enriched from OSM ({100*n_matched/total:.1f}%)")
    if n_matched > 0:
        code_counts = parcels.loc[matched, 'state_cd'].value_counts()
        for code, n in code_counts.items():
            print(f"   {code}: {n:,}")

    print(f"[write] Writing {args.output}…")
    t0 = time.time()
    parcels.to_file(args.output, driver='GeoJSON')
    print(f"[write] Done in {time.time()-t0:.1f}s "
          f"({os.path.getsize(args.output)/1024/1024:.1f} MB)")


if __name__ == '__main__':
    main()
