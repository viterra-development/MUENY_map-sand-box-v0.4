## Pendant Cluster Feeder AADT Propagation Plan

### 1. Context & Problem Statement

Phase 1.6 moved pendant-cluster detection onto the split-level primal graph, eliminating the self-referential entry-parent bug. However, after mapping cluster membership back to parent `LinearId`s, many feeders still contribute zero AADT. The issue is sequencing: feeder parents without static detector counts receive their modeled AADT at the same time (or after) their pendant children. When `ApplyPendantClusterConstraint` runs, those feeder parents have not yet been capped or even populated, so `totalEntryAadt` remains zero and the constraint is skipped. Logs such as “Pendant cluster … has zero entry AADT … Entry parents: 2” confirm the topology is correct but the data pipeline starves the cap calculation.

### 2. Goals

- Ensure every feeder parent surfaced by `IdentifyPendantClusters` has a non-zero AADT before constraining its descendant cluster.
- Apply pendant caps exactly once, in a deterministic order, without re-running the entire estimation pipeline.
- Preserve the split-level topology guarantees from the Full-Fidelity Primal Graph refactor while fixing the feeder AADT propagation gap.

### 3. Proposed Solution Overview

1. Separate baseline AADT estimation from pendant-cap propagation.
2. Traverse pendant clusters in increasing depth (outermost to innermost) so feeders are always processed before the clusters they supply.
3. Maintain a mutable lookup keyed by parent `LinearId` that records the best-known AADT (measured, modeled, then capped). Use this table for both feeder lookups and post-cap writes.
4. Add diagnostics to surface missing feeders so we can address data hygiene issues (duplicates, filtered roads) instead of silently skipping caps.

### 4. Implementation Plan

#### A. Baseline Estimation Table
- After the initial estimation pass (before topology corrections), build `Dictionary<string, int> parentAadt` containing every road’s current estimate (measured AADT overrides modeled values when present).
- Include roads previously filtered as duplicates so that any feeder appearing in `EntryParentSegments` resolves to an entry.

#### B. Cluster Ordering by Depth
- Extend `IdentifyPendantClusters` to produce a list of clusters with their `Depth` already recorded (this exists today).
- Before applying constraints, sort clusters by ascending `Depth`. For equal depths, stable ordering is sufficient because their feeders live outside the cluster.

#### C. One-Pass Pendant Cap Propagation
- For each sorted cluster:
  - Look up each `entryParentId` in `parentAadt`.
  - If a feeder is missing, emit a warning with the cluster ID, linear ID, and whether the road was filtered.
  - Sum the feeder values to compute `totalEntryAadt` and derive the capacity using the existing flow ratio attenuation.
  - Clamp each `clusterParentId` if its current value exceeds the computed capacity and immediately write the capped value back into `parentAadt`.
- Store the cap outcome (original, capped, reason) so the topology validator can keep emitting detailed warnings.

#### D. Road Object Synchronization
- After processing all clusters, push the updated values from `parentAadt` back into the corresponding `RoadSegment.Estimation.EstimatedAadt` fields so downstream workflows see the capped numbers.

#### E. Diagnostics & Instrumentation
- Log feeder lookups that resolve to zero, including whether the parent road lacked an estimate or was missing entirely.
- Add counters for “clusters capped”, “clusters skipped due to missing feeder”, and “clusters with mixed self/feeder parents” for telemetry.

### 5. Validation Strategy
- Re-run the Oak Dr / Johnson Bend scenario; expect the northbound road to cap at ~80 % of the east/west feeder’s AADT.
- Craft synthetic unit tests covering:
  - Single feeder with modeled AADT only.
  - Nested clusters (depth > 1) to confirm depth ordering applies caps transitively.
  - Missing feeder entry to verify logging and skip behavior fire correctly.
- Spot-check GeoJSON / warning outputs to verify entry feeders report the correct road names and capped values.

### 6. Risks & Mitigations
- **Duplicate handling regressions:** Pulling duplicate roads back into `parentAadt` could reintroduce double-counting. Mitigate by flagging duplicates in the lookup while still exposing their AADT for feeder use.
- **Performance:** Sorting clusters and iterating once adds negligible overhead relative to Tarjan + block-cut tree construction. Monitor timing in large counties to confirm.
- **Data Gaps:** If feeders systematically lack baseline estimates, caps will continue to skip. Treat the new diagnostics as actionable signals to fix upstream data quality.


