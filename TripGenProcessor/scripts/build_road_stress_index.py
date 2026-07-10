#!/usr/bin/env python3
"""
build_road_stress_index — county-wide Road Stress Index from fresh trip-gen data
=================================================================================
Reimplements the stress-index math from scripts/parcel_road_assigner.py
(steps 7-28) but takes ALL city parcel files at once, so roads that serve
multiple cities aggregate correctly and the output covers the whole county.

Pipeline:
  1. Load county road network with traffic attributes.
  2. Load + concat every *-parcels-with-trips.geojson passed in.
  3. Nearest-road assignment per parcel centroid (STRtree, max 200m).
  4. Aggregate daily/AM/PM trips + parcel counts onto roads.
  5. peak_to_average_ratio, land_use_entropy (Shannon), structural score.
  6. measured_aadt from the road's traffic dict.
  7. Traffic dominance (modeled/measured ratio) → dominance weight.
  8. rec_v2 = (entropy × peak_ratio × dominance_weight) / structural_score.
  9. road_stress_index = rec_v2 × ln(traffic_load + 1) × capacity_factor.
 10. Tier by quantiles of the nonzero distribution (75/90/97).
 11. Slim to map-essential fields, write map GeoJSON.

Usage:
    python3 build_road_stress_index.py \
        --roads   .../parker-roads-with-traffic.geojson \
        --parcels .../willow-park-parcels-with-trips.geojson [more ...] \
        --output  .../parker-county-road-stress-map.geojson
"""
import argparse
import ast
import json
import math
from collections import Counter
from pathlib import Path

import geopandas as gpd
import numpy as np
import pandas as pd
from shapely import STRtree

PROJ_CRS = "EPSG:2276"       # NAD83 / Texas North Central (US survey feet)
FT_TO_M = 0.3048
MAX_ASSIGN_DIST_M = 200

HIERARCHY_TO_SCORE = {1: 6, 2: 5, 3: 4, 4: 3, 5: 2, 6: 1}
MTFCC_TO_SCORE = {
    "S1100": 6, "S1200": 5, "S1500": 4, "S1400": 3,
    "S1630": 1, "S1640": 1, "S1730": 1, "S1740": 2,
    "S1750": 2, "S1780": 2, "S1820": 1,
}
DEFAULT_SCORE = 2
DOMINANCE_WEIGHTS = {
    "LOCAL_DOMINATED": 1.0, "MIXED": 0.6,
    "THROUGH_DOMINATED": 0.25, "UNKNOWN": 1.0,
}
MAP_FIELDS = [
    "fullName", "road_stress_index", "stress_tier", "rec_v2",
    "traffic_dominance_type", "structural_class_score", "measured_aadt",
    "total_daily_trips", "parcel_count", "peak_to_average_ratio",
    "land_use_entropy", "geometry",
]


def parse_maybe_dict(val):
    if val is None or (isinstance(val, float) and math.isnan(val)):
        return None
    if isinstance(val, dict):
        return val
    s = str(val)
    if not s or s == "nan":
        return None
    for parser in (json.loads, ast.literal_eval):
        try:
            out = parser(s)
            return out if isinstance(out, dict) else None
        except (ValueError, SyntaxError):
            continue
    return None


def structural_score(row):
    cls = parse_maybe_dict(row.get("classification"))
    if cls and cls.get("hierarchy") in HIERARCHY_TO_SCORE:
        return HIERARCHY_TO_SCORE[cls["hierarchy"]]
    mtfcc = str(row.get("mtfcc", ""))
    return MTFCC_TO_SCORE.get(mtfcc, DEFAULT_SCORE)


def shannon_entropy(counts):
    total = sum(counts.values())
    if total <= 1:
        return 0.0
    ent = 0.0
    for c in counts.values():
        p = c / total
        if p > 0:
            ent -= p * math.log(p)
    return ent


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roads", required=True)
    ap.add_argument("--parcels", nargs="+", required=True)
    ap.add_argument("--output", required=True)
    args = ap.parse_args()

    roads = gpd.read_file(args.roads)
    print(f"[roads] {len(roads):,} segments")

    frames = []
    for path in args.parcels:
        gdf = gpd.read_file(path)
        gdf["source_city_file"] = Path(path).name
        frames.append(gdf)
        print(f"[parcels] {Path(path).name}: {len(gdf):,}")
    parcels = pd.concat(frames, ignore_index=True)
    parcels = gpd.GeoDataFrame(parcels, crs=frames[0].crs)
    print(f"[parcels] combined: {len(parcels):,}")

    roads_proj = roads.to_crs(PROJ_CRS)
    parcels_proj = parcels.to_crs(PROJ_CRS)
    centroids = parcels_proj.geometry.centroid

    road_geoms = roads_proj.geometry.values
    tree = STRtree(road_geoms)
    nearest_idx, nearest_dist = [], []
    for c in centroids:
        i = tree.nearest(c)
        nearest_idx.append(i)
        nearest_dist.append(c.distance(road_geoms[i]) * FT_TO_M)
    parcels_proj["nearest_road_idx"] = nearest_idx
    parcels_proj["dist_to_road_m"] = nearest_dist
    assigned = parcels_proj[parcels_proj["dist_to_road_m"] <= MAX_ASSIGN_DIST_M]
    print(f"[assign] {len(assigned):,}/{len(parcels_proj):,} parcels within {MAX_ASSIGN_DIST_M}m of a road")

    # Aggregate trips
    for col in ("total_daily_trips", "total_am_peak", "total_pm_peak"):
        roads_proj[col] = 0.0
    roads_proj["parcel_count"] = 0
    agg = assigned.groupby("nearest_road_idx").agg(
        total_daily_trips=("daily_trips", "sum"),
        total_am_peak=("am_peak_trips", "sum"),
        total_pm_peak=("pm_peak_trips", "sum"),
        parcel_count=("daily_trips", "count"),
    )
    for ridx, row in agg.iterrows():
        roads_proj.at[ridx, "total_daily_trips"] = row["total_daily_trips"]
        roads_proj.at[ridx, "total_am_peak"] = row["total_am_peak"]
        roads_proj.at[ridx, "total_pm_peak"] = row["total_pm_peak"]
        roads_proj.at[ridx, "parcel_count"] = int(row["parcel_count"])

    # Peak-to-average
    def peak_avg(row):
        if row["total_daily_trips"] > 0:
            return max(row["total_am_peak"], row["total_pm_peak"]) / (row["total_daily_trips"] / 24)
        return 0.0
    roads_proj["peak_to_average_ratio"] = roads_proj.apply(peak_avg, axis=1)

    # Land-use entropy
    lu_by_road = {}
    for _, prow in assigned.iterrows():
        ridx = prow["nearest_road_idx"]
        lu = prow.get("ite_land_use") or "Unknown"
        lu_by_road.setdefault(ridx, Counter())[lu] += 1
    roads_proj["land_use_entropy"] = 0.0
    for ridx, counts in lu_by_road.items():
        roads_proj.at[ridx, "land_use_entropy"] = shannon_entropy(counts)

    # Structural score
    roads_proj["structural_class_score"] = roads_proj.apply(structural_score, axis=1)

    # measured_aadt from traffic dict
    def get_aadt(val):
        d = parse_maybe_dict(val)
        if d and d.get("aadt"):
            try:
                return float(d["aadt"])
            except (TypeError, ValueError):
                return None
        return None
    roads_proj["measured_aadt"] = roads_proj.get("traffic", pd.Series([None] * len(roads_proj))).apply(get_aadt)

    # Dominance
    def dominance(row):
        m = row["measured_aadt"]
        if m is None or pd.isna(m) or m <= 0:
            return "UNKNOWN"
        if row["total_daily_trips"] <= 0:
            return "UNKNOWN"
        ratio = row["total_daily_trips"] / m
        if ratio >= 0.50:
            return "LOCAL_DOMINATED"
        if ratio >= 0.10:
            return "MIXED"
        return "THROUGH_DOMINATED"
    roads_proj["traffic_dominance_type"] = roads_proj.apply(dominance, axis=1)
    roads_proj["dominance_weight"] = roads_proj["traffic_dominance_type"].map(DOMINANCE_WEIGHTS)

    # rec_v2 + stress index
    roads_proj["rec_v2"] = (
        roads_proj["land_use_entropy"]
        * roads_proj["peak_to_average_ratio"]
        * roads_proj["dominance_weight"]
    ) / roads_proj["structural_class_score"].clip(lower=1)

    roads_proj["traffic_load_used"] = roads_proj.apply(
        lambda r: r["measured_aadt"]
        if pd.notna(r["measured_aadt"]) and (r["measured_aadt"] or 0) > 0
        else r["total_daily_trips"],
        axis=1,
    )
    roads_proj["capacity_factor"] = roads_proj["structural_class_score"].apply(lambda s: max(1, 6 - s))
    roads_proj["road_stress_index"] = (
        roads_proj["rec_v2"]
        * np.log(roads_proj["traffic_load_used"].astype(float) + 1)
        * roads_proj["capacity_factor"]
    )

    nz = roads_proj.loc[
        (roads_proj["parcel_count"] > 0) & (roads_proj["road_stress_index"] > 0),
        "road_stress_index",
    ]
    q75, q90, q97 = nz.quantile(0.75), nz.quantile(0.90), nz.quantile(0.97)
    def tier(v):
        if v >= q97: return "CRITICAL"
        if v >= q90: return "HIGH"
        if v >= q75: return "MODERATE"
        return "LOW"
    roads_proj["stress_tier"] = roads_proj["road_stress_index"].apply(tier)

    print(f"[stress] thresholds  MODERATE>={q75:.3f}  HIGH>={q90:.3f}  CRITICAL>={q97:.3f}")
    tiers = roads_proj.loc[roads_proj["parcel_count"] > 0, "stress_tier"].value_counts()
    for t in ("CRITICAL", "HIGH", "MODERATE", "LOW"):
        print(f"  {t:9s}: {tiers.get(t, 0):,}")

    # Slim + write
    out = roads_proj.loc[
        (roads_proj["parcel_count"] > 0) | roads_proj["measured_aadt"].notna()
    ].copy()
    out = out.to_crs("EPSG:4326")
    keep = [c for c in MAP_FIELDS if c in out.columns]
    out = out[keep]
    out.to_file(args.output, driver="GeoJSON")
    size_mb = Path(args.output).stat().st_size / 1024 / 1024
    print(f"[write] {args.output} — {len(out):,} features, {size_mb:.1f} MB")


if __name__ == "__main__":
    main()
