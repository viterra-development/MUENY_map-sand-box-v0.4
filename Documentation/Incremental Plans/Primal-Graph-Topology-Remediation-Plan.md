# Primal Graph Topology Remediation Plan

## 1. Objective

Restore the Phase 1.6 topology pipeline so that single-entry subdivision clusters receive correct traffic caps. This requires building an accurate primal graph (intersections as vertices, split segments as edges) and running pendant-cluster detection and flow constraints solely on that graph.

## 2. Key Findings Driving the Work

- **Missing segment splits:** `BuildPrimalGraph` only associates a road segment with the first/last intersection of its TIGER polyline. Internal intersections appear in the adjacency list but have empty `ConnectedSegments`, yielding zero feeder traffic and preventing pendant caps from triggering.
- **Dual graph models in use:** After constructing the primal graph the pipeline rebuilds the legacy segment-level adjacency (`BuildAdjacencyGraph`). Phase 1.6 uses the partial primal graph while dead-end and low-degree checks still depend on the legacy graph, hiding defects and complicating debugging.
- **Debug evidence:** `primal-graph-debug.txt` shows articulation intersections reporting `Connected segments: 0` despite multiple adjacent intersections, proving the segment-to-intersection mapping is incomplete.

## 3. High-Level Approach

1. **Rebuild the primal graph correctly**
   - Split each polyline into sub-segments between consecutive intersection vertices.
   - For every intersection, populate `ConnectedSegments` and `IntersectionToSegments` with those split records (preserving parent `LinearId`).
   - Maintain mappings back to the original segment (parent id, length fraction, geometry references if needed).

2. **Refactor topology constraints to rely solely on the primal graph**
   - Update pendant-cluster, dead-end, and low-connectivity rules to consume the split-segment data.
   - Retire or hide the legacy graph once validation confirms parity.

3. **Instrument and validate**
   - Extend logging/debug tooling to show intersection degree, feeder totals, and cluster depth.
   - Add automated checks that assert cluster capacity equals feeder sum × ratio and that graph invariants hold.
   - Re-run Phase 1.6 on Parker County to confirm the Oak Dr / Johnson Bend case caps near ~740 AADT.

## 4. Detailed Task Breakdown

### Phase A – Data Model & Splitting

- Design a `SegmentSplit` structure (`ParentLinearId`, `StartIntersectionId`, `EndIntersectionId`, length fraction, optional geometry pointer).
- Modify `BuildPrimalGraph` to:
  - Identify all intersection indices along each polyline.
  - Create split records between consecutive intersection indices.
  - Populate `IntersectionToSegments`, `SegmentToIntersections`, and `SegmentToAllIntersections` with split-aware data.
- Ensure every intersection’s `ConnectedSegments` count matches its adjacency degree.

### Phase B – Constraint Engine Update

- Update `ApplyPendantClusterConstraint` to:
  - Sum feeder AADT from split segments whose parents fall outside the cluster.
  - Apply caps to cluster members based on the split IDs, while logging the parent road names in warnings.
- Adapt dead-end and low-connectivity rules to operate on intersection degree from the split graph.
- Introduce a feature flag to disable the legacy adjacency build after validation.

### Phase C – Instrumentation & QA

- Enhance `DumpGraphToFile` (or add targeted debug commands) to report per-intersection feeder vs. cluster segment counts.
- Integrate synthetic fixtures for unit/integration tests covering:
  - Single-entry cluster with one feeder.
  - Nested clusters with exponential attenuation (depth > 1).
  - Multi-feeder intersection where total capacity is the sum of feeders.
- Re-run full Parker County processing and compare key scenes (Oak Dr, Adair Ln, etc.) against expected capped values.

## 5. Deliverables

- Updated `NetworkTopologyValidator` featuring split-segment primal graph construction.
- Revised topology constraint logic with accurate pendant cluster enforcement.
- New or updated automated tests plus validation report confirming Oak Dr remediation.
- Regenerated `parker-roads-with-traffic-phase1.geojson` and supporting quality reports.
- Developer notes detailing the split-segment model and debugging workflow.

## 6. Validation Checklist

- ✅ Oak Dr cluster caps to ~740 AADT (80 % of Johnson Bend Rd’s 925 AADT).
- ✅ Pendant clusters report non-zero entry segments; logs include feeder totals and cluster depth.
- ✅ Graph invariants: every split is referenced by two intersections, and intersection degree equals `ConnectedSegments.Count`.
- ✅ Automated tests covering single-entry, nested, and multi-feeder cases pass.
- ✅ Graph construction and constraint passes remain within performance targets (<10 s build, <5 s constraint on Parker dataset).

## 7. Risks & Mitigations

- **Segment explosion:** Splitting increases edge count. Keep split structs lightweight and avoid duplicating geometry. Profile memory usage after implementation.
- **Downstream compatibility:** Some code paths expect original `LinearId`s. Preserve parent references and expose split IDs only within topology logic.
- **Regression from removing legacy graph:** Maintain a feature flag or temporary parity checks until validation reports confirm the new pipeline matches or improves prior behavior.


