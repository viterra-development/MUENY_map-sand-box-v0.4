# Full-Fidelity Primal Graph Refactor Plan

## 1. Context & Root Cause

During Phase 1.6 we generate split-level edges (`SegmentSplit`) to form a proper primal graph. However, when Tarjan’s biconnected-component pass finishes we collapse membership back to parent `LinearId`s immediately. Because the feeder road’s polyline touches the articulation intersection, its parent ID lands inside the component set, so the pendant-cluster detector later treats the feeder as “inside” and filters it out. Consequently, `EntrySegments` is empty, `totalEntryAadt` stays zero, and the new capacity cap never fires. The fix is to keep Tarjan and the block-cut tree operating purely on split IDs, then map back to parent roads only after we know which splits lie in a cluster and which belong to the feeder.

## 2. Goals

1. Run Tarjan’s algorithm and block-cut tree construction solely against split-level edges.
2. Preserve split-based cluster membership through pendant-cluster detection.
3. Convert to parent `LinearId`s only at the handoff points (AATD summing, warnings, GeoJSON output).
4. Eliminate the “feeder counted as cluster member” bug and restore correct pendant caps (e.g. Oak Dr capped by Johnson Bend).

## 3. Planned Changes

### A. Adjust biconnected-component mapping
- Change `FindBiconnectedComponentsOnPrimalGraph` to store split IDs (`splitId`) in `component.Segments` instead of parent `LinearId`s.
- Maintain a parallel map for convenience: `component.ParentSegments = matchingSplitIds.Select(split => split.ParentLinearId).Distinct()` when we need road-level summaries.

### B. Update block-cut tree & cluster detection
- Modify `BlockCutTree.Segments` to hold split IDs.
- In `IdentifyPendantClusters`, build `clusterSplitIds` from the tree directly. Derive `clusterParentIds` lazily via `.Select(split => SplitSegments[split].ParentLinearId)` when necessary.
- Determine entry feeders using split IDs: `entrySplitIds = allSplitsAtEntry.Except(clusterSplitIds)`. Only after this step convert to parent road IDs for AADT lookup.

### C. Pendant-cap calculation
- In `ApplyPendantClusterConstraint`, sum feeder AADT via parent IDs from the `entrySplitIds`. Track which splits fed the total for debugging.
- When writing warnings, include both the parent road name and (optionally) the contributing split count for clarity.

### D. Data structures & telemetry
- Extend `PendantCluster` to hold `ClusterSplitIds` and `EntrySplitIds` alongside parent ID lists.
- Update `DumpGraphToFile` (and any targeted debug commands) to report connected split counts vs. parent counts so we can confirm feeders live outside cluster membership.

### E. Validation
- Re-run Oak Dr / Johnson Bend scenario; expect entry feeders to report Johnson Bend’s split(s) and cluster to cap near 740 AADT (80% of 925).
- Create or refresh automated tests using small synthetic graphs: single feeder, dual feeder, nested clusters.
- Spot-check GeoJSON warnings to ensure articulation entries reference the true feeder parent road.

## 4. Reasoning Summary

Tarjan’s algorithm sees a graph of intersections linked by edges; those edges are the split segments we generate. By collapsing to parent `LinearId`s too early we reintroduce the ambiguity we set out to remove: a single parent polyline can straddle both sides of the articulation point, so it shouldn’t be classified wholesale as “inside” the cluster. Keeping the split IDs as the authoritative membership fixes that: a feeder contributes at least one split that leaves the cluster via the articulation intersection, and that split remains visible until the very end. Only after the topology computation is finished do we aggregate the splits back up to parent roads for AADT lookups and reporting. This ensures feeders stay feeders, cluster caps get the correct supply traffic, and the pendant constraint finally enforces conservation of flow.


