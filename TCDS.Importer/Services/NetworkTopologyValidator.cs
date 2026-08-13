using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using MapSandBox.Shared.Services;

namespace TCDS.Importer.Services;

/// <summary>
/// Phase 1.5: Network topology validation to prevent absurd estimates
/// (e.g., dead-end roads with highway-level traffic)
/// </summary>
public class NetworkTopologyValidator
{
    private readonly ILogger<NetworkTopologyValidator> _logger;
    private readonly RoadGeometryService _roadGeometryService;
    private readonly double _endpointTolerance = 5.0; // meters tolerance for endpoint matching
    private readonly double _overlapThreshold = 0.8; // 80% overlap to consider roads as duplicates

    // Network graph data structures
    private Dictionary<string, List<string>> _adjacencyGraph = new();
    private Dictionary<string, int> _connectivityDegree = new();
    private HashSet<string> _deadEndRoads = new();
    private HashSet<string> _isolatedRoads = new();

    // Endpoint storage for spatial matching
    private Dictionary<string, List<GeoPoint>> _roadEndpoints = new();

    // Duplicate road tracking
    private Dictionary<string, string> _duplicateToCanonical = new(); // Maps duplicate linearId to canonical linearId
    private HashSet<string> _duplicateRoads = new(); // Set of duplicate linearIds to exclude from topology

    // Phase 1.6: Articulation point detection and single-entry cluster tracking
    private HashSet<string> _articulationPoints = new(); // Set of articulation points (cut vertices)
    private Dictionary<string, HashSet<string>> _singleEntryClusters = new(); // Entry road -> cluster member roads
    private Dictionary<string, string> _roadToCluster = new(); // Road -> cluster entry road mapping

    // Phase 1.6: Primal graph (intersections as vertices)
    private PrimalGraph? _primalGraph;
    private List<BiconnectedComponent> _biconnectedComponents = new();
    private BlockCutTree? _blockCutTree;
    private List<PendantCluster> _pendantClusters = new();

    // Configuration
    private readonly TopologyConstraintConfig _config = new();

    public NetworkTopologyValidator(ILogger<NetworkTopologyValidator> logger, RoadGeometryService roadGeometryService)
    {
        _logger = logger;
        _roadGeometryService = roadGeometryService;
    }

    /// <summary>
    /// Build network graph from road segment geometries
    /// </summary>
    public void BuildNetworkGraph(List<RoadSegment> allRoads)
    {
        _logger.LogInformation("Building network graph for {Count} road segments", allRoads.Count);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Step 0: Detect and consolidate duplicate roads (same geometry, different names)
        DetectDuplicateRoads(allRoads);

        // Phase 1.6: Build primal graph (intersections as vertices)
        _primalGraph = BuildPrimalGraph(allRoads);

        // Phase 1.6: Find biconnected components on primal graph
        HashSet<string> articulationPoints;
        (articulationPoints, _biconnectedComponents) = FindBiconnectedComponentsOnPrimalGraph(_primalGraph);

        // Phase 1.6: Build block-cut tree
        _blockCutTree = BuildBlockCutTree(_primalGraph, articulationPoints, _biconnectedComponents);

        // Phase 1.6: Identify pendant clusters (subdivisions with single entry points)
        _pendantClusters = IdentifyPendantClusters(_blockCutTree, _primalGraph);

        // Legacy Phase 1.5 methods (for backward compatibility and comparison)
        // TODO: Remove these once Phase 1.6 is validated
        // Step 1: Extract all endpoints
        ExtractEndpoints(allRoads);

        // Step 2: Build spatial index of endpoints
        BuildEndpointIndex();

        // Step 3: Find connections between roads
        BuildAdjacencyGraph(allRoads);

        // Step 4: Analyze connectivity
        AnalyzeConnectivity();

        // Step 5: Find articulation points (Phase 1.5 - segment-based)
        FindArticulationPoints();

        // Step 6: Identify single-entry clusters (Phase 1.5 - segment-based)
        IdentifySingleEntryClusters(allRoads);

        sw.Stop();
        _logger.LogInformation("Network graph built in {Elapsed:F2}s", sw.Elapsed.TotalSeconds);
        _logger.LogInformation("  - Total roads: {Total}", allRoads.Count);
        _logger.LogInformation("  - Dead-end roads: {DeadEnds}", _deadEndRoads.Count);
        _logger.LogInformation("  - Isolated roads: {Isolated}", _isolatedRoads.Count);
        _logger.LogInformation("  - Average connectivity: {AvgConnectivity:F2}",
            _connectivityDegree.Values.Average());
        _logger.LogInformation("  - Articulation points (cut vertices): {ArticulationPoints}", _articulationPoints.Count);
        _logger.LogInformation("  - Single-entry clusters: {Clusters}", _singleEntryClusters.Count);
    }

    /// <summary>
    /// Get pendant clusters identified during network graph building
    /// </summary>
    public List<PendantCluster> GetPendantClusters()
    {
        return _pendantClusters;
    }

    /// <summary>
    /// Detect and consolidate duplicate roads (same geometry, different names)
    /// Example: FM 2421 and Zion Hill Rd both have identical 417 coordinates
    /// </summary>
    private void DetectDuplicateRoads(List<RoadSegment> allRoads)
    {
        _logger.LogInformation("Detecting duplicate roads with overlapping geometries...");

        var duplicateGroups = new List<List<RoadSegment>>();
        var processedRoads = new HashSet<string>();

        // Check all pairs of roads for geometry similarity
        for (int i = 0; i < allRoads.Count; i++)
        {
            var road1 = allRoads[i];
            if (processedRoads.Contains(road1.LinearId))
                continue;

            var group = new List<RoadSegment> { road1 };
            processedRoads.Add(road1.LinearId);

            for (int j = i + 1; j < allRoads.Count; j++)
            {
                var road2 = allRoads[j];
                if (processedRoads.Contains(road2.LinearId))
                    continue;

                // Check if roads have identical or nearly identical geometries
                if (AreGeometriesDuplicate(road1, road2))
                {
                    group.Add(road2);
                    processedRoads.Add(road2.LinearId);
                }
            }

            if (group.Count > 1)
            {
                duplicateGroups.Add(group);
            }
        }

        // Process each duplicate group: pick canonical road and mark others as duplicates
        int totalDuplicates = 0;
        foreach (var group in duplicateGroups)
        {
            var canonical = SelectCanonicalRoad(group);

            foreach (var road in group)
            {
                if (road.LinearId != canonical.LinearId)
                {
                    _duplicateToCanonical[road.LinearId] = canonical.LinearId;
                    _duplicateRoads.Add(road.LinearId);
                    totalDuplicates++;

                    _logger.LogDebug("Duplicate detected: {Duplicate} ({DupName}) -> {Canonical} ({CanName})",
                        road.LinearId, road.FullName, canonical.LinearId, canonical.FullName);
                }
            }
        }

        _logger.LogInformation("  ✓ Detected {Groups} duplicate groups with {Total} duplicate roads",
            duplicateGroups.Count, totalDuplicates);
    }

    /// <summary>
    /// Check if two roads have identical or nearly identical geometries
    /// </summary>
    private bool AreGeometriesDuplicate(RoadSegment road1, RoadSegment road2)
    {
        var coords1 = GetCoordinates(road1);
        var coords2 = GetCoordinates(road2);

        // Must have same number of coordinates
        if (coords1.Count != coords2.Count)
            return false;

        // If they have different number of points, they're not duplicates
        if (coords1.Count == 0)
            return false;

        // Check if all coordinates are identical (or very close)
        const double tolerance = 0.000001; // ~0.1 meters in decimal degrees
        int matchingPoints = 0;

        for (int i = 0; i < coords1.Count; i++)
        {
            var dist = Math.Sqrt(
                Math.Pow(coords1[i][0] - coords2[i][0], 2) +
                Math.Pow(coords1[i][1] - coords2[i][1], 2)
            );

            if (dist < tolerance)
            {
                matchingPoints++;
            }
        }

        // Consider duplicate if >= 95% of points match
        double matchPercentage = (double)matchingPoints / coords1.Count;
        return matchPercentage >= 0.95;
    }

    /// <summary>
    /// Get coordinates from a road segment's geometry
    /// </summary>
    private List<List<double>> GetCoordinates(RoadSegment road)
    {
        if (road.Geometry is LineStringGeometry lineString)
        {
            return lineString.Coordinates;
        }
        else if (road.Geometry is MultiLineStringGeometry multiLineString && multiLineString.Coordinates.Any())
        {
            return multiLineString.Coordinates.First();
        }
        return new List<List<double>>();
    }

    /// <summary>
    /// Select the canonical road from a group of duplicates
    /// Priority: state/federal highways > existing AADT > longer names
    /// </summary>
    private RoadSegment SelectCanonicalRoad(List<RoadSegment> group)
    {
        // Priority 1: Prefer state/federal highway designations
        var stateOrFederalHighways = group.Where(r =>
            r.FullName.StartsWith("FM ") ||
            r.FullName.StartsWith("US ") ||
            r.FullName.StartsWith("I-") ||
            r.FullName.StartsWith("I ") ||
            r.FullName.StartsWith("SH ")
        ).ToList();

        if (stateOrFederalHighways.Any())
        {
            // Among highways, prefer those with existing AADT data
            var withAadt = stateOrFederalHighways.Where(r => r.ExistingAadt.HasValue).ToList();
            if (withAadt.Any())
                return withAadt.OrderByDescending(r => r.ExistingAadt).First();

            return stateOrFederalHighways.First();
        }

        // Priority 2: Prefer roads with existing AADT data
        var roadsWithAadt = group.Where(r => r.ExistingAadt.HasValue).ToList();
        if (roadsWithAadt.Any())
        {
            return roadsWithAadt.OrderByDescending(r => r.ExistingAadt).First();
        }

        // Priority 3: Prefer longer/more descriptive names
        return group.OrderByDescending(r => r.FullName.Length).First();
    }

    /// <summary>
    /// Extract start/end points from road geometries
    /// </summary>
    private void ExtractEndpoints(List<RoadSegment> allRoads)
    {
        foreach (var road in allRoads)
        {
            // Skip duplicate roads
            if (_duplicateRoads.Contains(road.LinearId))
                continue;

            var endpoints = new List<GeoPoint>();

            if (road.Geometry is LineStringGeometry lineString && lineString.Coordinates.Any())
            {
                // Get first and last coordinates
                var first = lineString.Coordinates.First();
                var last = lineString.Coordinates.Last();

                endpoints.Add(new GeoPoint(first[0], first[1], road.LinearId));
                endpoints.Add(new GeoPoint(last[0], last[1], road.LinearId));
            }
            else if (road.Geometry is MultiLineStringGeometry multiLineString && multiLineString.Coordinates.Any())
            {
                // For MultiLineString, get first and last points of first line
                var firstLine = multiLineString.Coordinates.First();
                if (firstLine.Any())
                {
                    var first = firstLine.First();
                    var last = firstLine.Last();

                    endpoints.Add(new GeoPoint(first[0], first[1], road.LinearId));
                    endpoints.Add(new GeoPoint(last[0], last[1], road.LinearId));
                }
            }

            if (endpoints.Any())
            {
                _roadEndpoints[road.LinearId] = endpoints;
            }
        }

        _logger.LogDebug("Extracted endpoints for {Count} roads", _roadEndpoints.Count);
    }

    /// <summary>
    /// Build spatial index of all endpoints for fast proximity search
    /// Simple implementation without NetTopologySuite
    /// </summary>
    private void BuildEndpointIndex()
    {
        // Since we're not using a spatial index library, we'll just use the endpoints dictionary
        // The matching will be done with simple distance calculations
        _logger.LogDebug("Prepared {Count} endpoints for matching",
            _roadEndpoints.Sum(kvp => kvp.Value.Count));
    }

    /// <summary>
    /// Build adjacency graph by finding connected roads
    /// Enhanced to detect T-intersections and mid-road connections
    /// </summary>
    private void BuildAdjacencyGraph(List<RoadSegment> allRoads)
    {
        // Initialize adjacency lists (skip duplicates)
        foreach (var road in allRoads)
        {
            if (_duplicateRoads.Contains(road.LinearId))
                continue;

            _adjacencyGraph[road.LinearId] = new List<string>();
        }

        _logger.LogDebug("Building adjacency graph with T-intersection detection...");

        // For each road, check if its endpoints connect to ANY point on other roads
        // This catches T-intersections where dead-ends connect to through roads
        foreach (var kvp in _roadEndpoints)
        {
            var linearId = kvp.Key;
            var endpoints = kvp.Value;
            var road = allRoads.FirstOrDefault(r => r.LinearId == linearId);

            if (road == null)
                continue;

            var connectedRoads = new HashSet<string>();

            foreach (var endpoint in endpoints)
            {
                // Check all other roads
                foreach (var otherRoad in allRoads)
                {
                    // Skip self-connections
                    if (otherRoad.LinearId == linearId)
                        continue;

                    // Skip duplicate roads
                    if (_duplicateRoads.Contains(otherRoad.LinearId))
                        continue;

                    // Check if endpoint is near ANY coordinate point on the other road
                    // This detects T-intersections and mid-road connections
                    if (IsEndpointNearRoad(endpoint, otherRoad))
                    {
                        connectedRoads.Add(otherRoad.LinearId);
                    }
                }
            }

            _adjacencyGraph[linearId] = connectedRoads.ToList();
        }

        _logger.LogDebug("Built adjacency graph with {Edges} total connections (including T-intersections)",
            _adjacencyGraph.Sum(kvp => kvp.Value.Count));
    }

    /// <summary>
    /// Check if an endpoint is near any point on a road (detects T-intersections)
    /// </summary>
    private bool IsEndpointNearRoad(GeoPoint endpoint, RoadSegment road)
    {
        if (road.Geometry is LineStringGeometry lineString)
        {
            // Check if endpoint is within tolerance of any point on the road
            foreach (var coord in lineString.Coordinates)
            {
                var distance = CalculateDistanceMeters(
                    endpoint.Longitude, endpoint.Latitude,
                    coord[0], coord[1]);

                if (distance <= _endpointTolerance)
                {
                    return true;
                }
            }
        }
        else if (road.Geometry is MultiLineStringGeometry multiLineString && multiLineString.Coordinates.Any())
        {
            // For MultiLineString, check first line
            var firstLine = multiLineString.Coordinates.First();
            foreach (var coord in firstLine)
            {
                var distance = CalculateDistanceMeters(
                    endpoint.Longitude, endpoint.Latitude,
                    coord[0], coord[1]);

                if (distance <= _endpointTolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Analyze network connectivity and identify special cases
    /// </summary>
    private void AnalyzeConnectivity()
    {
        foreach (var kvp in _adjacencyGraph)
        {
            var linearId = kvp.Key;
            var connections = kvp.Value;

            var degree = connections.Count;
            _connectivityDegree[linearId] = degree;

            // Identify dead-ends (degree = 1)
            if (degree == 1)
            {
                _deadEndRoads.Add(linearId);
            }

            // Identify isolated roads (degree = 0)
            if (degree == 0)
            {
                _isolatedRoads.Add(linearId);
            }
        }
    }

    /// <summary>
    /// Get topology metrics for a specific road
    /// </summary>
    public TopologyMetrics GetTopologyMetrics(string linearId)
    {
        var connectedRoads = _adjacencyGraph.GetValueOrDefault(linearId, new List<string>());
        var degree = _connectivityDegree.GetValueOrDefault(linearId, 0);

        return new TopologyMetrics
        {
            IsDeadEnd = _deadEndRoads.Contains(linearId),
            ConnectivityDegree = degree,
            ConnectedRoads = connectedRoads,
            IsIsolated = _isolatedRoads.Contains(linearId)
        };
    }

    /// <summary>
    /// Apply topology constraints to correct absurd estimates
    /// </summary>
    public (AadtEstimation correctedEstimation, TopologyViolation? violation) ApplyTopologyConstraints(
        RoadSegment road,
        AadtEstimation initialEstimate,
        List<RoadSegment> allRoads)
    {
        var topology = GetTopologyMetrics(road.LinearId);
        var correctedAadt = initialEstimate.EstimatedAadt;
        var warnings = new List<string>(initialEstimate.Warnings);
        TopologyViolation? violation = null;

        // Phase 1.6: Pendant cluster constraint (HIGHEST PRIORITY)
        // This uses biconnected components analysis on the primal graph (intersections as vertices)
        var pendantClusterViolation = ApplyPendantClusterConstraint(road, ref correctedAadt, warnings, allRoads);
        if (pendantClusterViolation != null)
        {
            // Pendant cluster constraint applied - return immediately as this is the strongest constraint
            return (new AadtEstimation
            {
                EstimatedAadt = correctedAadt,
                Method = initialEstimate.Method + "_BiconnectedComponentConstrained",
                Confidence = initialEstimate.Confidence * 0.9,
                SourceRoads = initialEstimate.SourceRoads,
                Warnings = warnings,
                EstimationYear = initialEstimate.EstimationYear,
                Topology = topology
            }, pendantClusterViolation);
        }

        // Phase 1.5: Legacy single-entry cluster constraint (segment-based, for backward compatibility)
        // This overrides individual road constraints because it represents physical network limitations
        if (_roadToCluster.TryGetValue(road.LinearId, out var clusterEntryRoad))
        {
            // This road is part of a single-entry cluster
            var entryRoad = allRoads.FirstOrDefault(r => r.LinearId == clusterEntryRoad);
            if (entryRoad != null)
            {
                var entryRoadAadt = entryRoad.Estimation?.EstimatedAadt ?? entryRoad.ExistingAadt ?? 0;

                if (entryRoadAadt > 0)
                {
                    // Cap at 80% of cluster entry road's capacity
                    var clusterMax = (int)(entryRoadAadt * 0.8);

                    if (correctedAadt > clusterMax)
                    {
                        var originalAadt = correctedAadt;
                        correctedAadt = clusterMax;

                        var entryRoadName = entryRoad.FullName ?? entryRoad.LinearId;
                        var warning = $"Single-entry cluster road capped to {clusterMax:N0} AADT " +
                                    $"(80% of cluster entry road '{entryRoadName}': {entryRoadAadt:N0})";
                        warnings.Add(warning);

                        violation = new TopologyViolation
                        {
                            LinearId = road.LinearId,
                            RoadName = road.FullName,
                            ViolationType = "SingleEntryCluster",
                            OriginalEstimate = originalAadt,
                            CorrectedEstimate = correctedAadt,
                            Difference = originalAadt - correctedAadt,
                            PercentChange = originalAadt > 0 ? (originalAadt - correctedAadt) * 100.0 / originalAadt : 0,
                            Reason = warning
                        };

                        _logger.LogDebug("Single-entry cluster correction: {Road} from {Original:N0} to {Corrected:N0}",
                            road.FullName, originalAadt, correctedAadt);

                        // Return early - cluster constraint is the strongest constraint
                        return (new AadtEstimation
                        {
                            EstimatedAadt = correctedAadt,
                            Method = initialEstimate.Method,
                            Confidence = initialEstimate.Confidence * 0.9, // Slightly reduced confidence due to correction
                            SourceRoads = initialEstimate.SourceRoads,
                            Warnings = warnings
                        }, violation);
                    }
                }
            }
        }

        // Rule 1: Dead-end constraint
        if (topology.IsDeadEnd && topology.ConnectedRoads.Any())
        {
            var connectorRoads = topology.ConnectedRoads
                .Select(id => allRoads.FirstOrDefault(r => r.LinearId == id))
                .Where(r => r != null)
                .ToList();

            if (connectorRoads.Any())
            {
                // Get max AADT from connector roads
                var maxConnectorAadt = connectorRoads
                    .Select(r => r.Estimation?.EstimatedAadt ?? r.ExistingAadt ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();

                if (maxConnectorAadt > 0)
                {
                    // Cap at 80% of connector road traffic
                    var cappedAadt = (int)(maxConnectorAadt * 0.8);

                    if (correctedAadt > cappedAadt)
                    {
                        var originalAadt = correctedAadt;
                        correctedAadt = cappedAadt;

                        var warning = $"Dead-end road capped to {cappedAadt:N0} AADT " +
                                    $"(80% of connector road max: {maxConnectorAadt:N0})";
                        warnings.Add(warning);

                        violation = new TopologyViolation
                        {
                            LinearId = road.LinearId,
                            RoadName = road.FullName,
                            ViolationType = "DeadEnd",
                            OriginalEstimate = originalAadt,
                            CorrectedEstimate = correctedAadt,
                            Difference = originalAadt - correctedAadt,
                            PercentChange = (originalAadt - correctedAadt) * 100.0 / originalAadt,
                            Reason = warning
                        };

                        _logger.LogDebug("Dead-end correction: {Road} from {Original:N0} to {Corrected:N0}",
                            road.FullName, originalAadt, correctedAadt);
                    }
                }
            }
        }

        // Rule 2: Low connectivity constraint (degree = 2, not a through route)
        if (topology.ConnectivityDegree == 2 && !topology.IsDeadEnd && correctedAadt > 5000)
        {
            var connectedRoads = topology.ConnectedRoads
                .Select(id => allRoads.FirstOrDefault(r => r.LinearId == id))
                .Where(r => r != null)
                .ToList();

            if (connectedRoads.Any())
            {
                var maxConnected = connectedRoads
                    .Select(r => r.Estimation?.EstimatedAadt ?? r.ExistingAadt ?? int.MaxValue)
                    .DefaultIfEmpty(int.MaxValue)
                    .Max();

                // Allow 20% increase over connected roads (for through traffic)
                var cappedAadt = (int)(maxConnected * 1.2);

                if (correctedAadt > cappedAadt && maxConnected < int.MaxValue)
                {
                    var originalAadt = correctedAadt;
                    correctedAadt = cappedAadt;

                    var warning = $"Low-connectivity road capped to {cappedAadt:N0} AADT " +
                                $"(120% of max connected road: {maxConnected:N0})";
                    warnings.Add(warning);

                    if (violation == null) // Only create if not already set by dead-end rule
                    {
                        violation = new TopologyViolation
                        {
                            LinearId = road.LinearId,
                            RoadName = road.FullName,
                            ViolationType = "LowConnectivity",
                            OriginalEstimate = originalAadt,
                            CorrectedEstimate = correctedAadt,
                            Difference = originalAadt - correctedAadt,
                            PercentChange = (originalAadt - correctedAadt) * 100.0 / originalAadt,
                            Reason = warning
                        };
                    }

                    _logger.LogDebug("Low-connectivity correction: {Road} from {Original:N0} to {Corrected:N0}",
                        road.FullName, originalAadt, correctedAadt);
                }
            }
        }

        // Rule 3: Isolated road fallback
        if (topology.IsIsolated)
        {
            warnings.Add("Isolated road with no connections - estimate may be unreliable");
        }

        // Create corrected estimation
        var corrected = new AadtEstimation
        {
            EstimatedAadt = correctedAadt,
            Method = correctedAadt == initialEstimate.EstimatedAadt
                ? initialEstimate.Method
                : $"{initialEstimate.Method}_TopologyConstrained",
            Confidence = initialEstimate.Confidence *
                (correctedAadt == initialEstimate.EstimatedAadt ? 1.0 : 0.95), // Slight reduction for corrected
            SourceRoads = initialEstimate.SourceRoads,
            Warnings = warnings,
            EstimationYear = initialEstimate.EstimationYear,
            Topology = topology
        };

        return (corrected, violation);
    }

    /// <summary>
    /// Apply pendant cluster constraint with nested attenuation using split-based membership
    /// </summary>
    private TopologyViolation? ApplyPendantClusterConstraint(
        RoadSegment road,
        ref int correctedAadt,
        List<string> warnings,
        List<RoadSegment> allRoads)
    {
        // Check if this road (parent LinearId) is in any pendant cluster
        var cluster = _pendantClusters.FirstOrDefault(c => c.ClusterParentSegments.Contains(road.LinearId));
        if (cluster == null)
            return null;

        // Get total AADT from all entry PARENT segments (unique feeder roads from main network)
        double totalEntryAadt = 0;
        var entryRoadNames = new List<string>();

        foreach (var entryParentId in cluster.EntryParentSegments)
        {
            var entryRoad = allRoads.FirstOrDefault(r => r.LinearId == entryParentId);
            if (entryRoad != null)
            {
                var aadt = entryRoad.Estimation?.EstimatedAadt
                        ?? entryRoad.ExistingAadt
                        ?? 0;

                totalEntryAadt += aadt;
                entryRoadNames.Add(entryRoad.FullName ?? entryParentId);

                // Debug logging for specific roads
                if (road.FullName?.Contains("Oak Dr") == true || road.FullName?.Contains("Johnson Bend") == true)
                {
                    _logger.LogDebug("  Entry parent for {Road} ({LinearId}): {EntryRoad} ({EntryId}) with {AADT} AADT",
                        road.FullName, road.LinearId, entryRoad.FullName, entryParentId, aadt);
                }
            }
        }

        if (totalEntryAadt == 0)
        {
            // Log detailed debug info when no entry AADT is found
            _logger.LogWarning("Pendant cluster at {Entry} has zero entry AADT for road {Road}. Entry splits: {EntrySplits}, Entry parents: {EntryParents}",
                cluster.EntryIntersectionId, road.FullName,
                cluster.EntrySplitSegments.Count, cluster.EntryParentSegments.Count);
            return null;
        }

        // Calculate cluster capacity with depth-based attenuation
        double attenuationFactor;
        if (_config.UseNestedAttenuation && cluster.Depth > 0)
        {
            // Exponential attenuation: 0.8^depth
            // Models cumulative flow restriction through nested articulation points
            attenuationFactor = Math.Pow(_config.FlowConstraintRatio, cluster.Depth);
        }
        else
        {
            // Simple ratio (no nesting consideration)
            attenuationFactor = _config.FlowConstraintRatio;
        }

        var clusterCapacity = (int)(totalEntryAadt * attenuationFactor);

        // Check if we need to apply constraint
        if (correctedAadt <= clusterCapacity)
            return null;

        // Apply constraint
        var originalAadt = correctedAadt;
        correctedAadt = clusterCapacity;

        // Build warning message with depth and entry road information
        var entryRoadDescription = entryRoadNames.Count == 1
            ? $"entry road '{entryRoadNames[0]}'"
            : $"{entryRoadNames.Count} entry roads ({string.Join(", ", entryRoadNames.Take(2))}{(entryRoadNames.Count > 2 ? ", ..." : "")})";

        var warningMsg = cluster.Depth > 1
            ? $"Nested pendant cluster (depth {cluster.Depth}): Capped to {clusterCapacity:N0} AADT " +
              $"({attenuationFactor:P0} of {entryRoadDescription}: {totalEntryAadt:N0})"
            : $"Pendant cluster constraint: Capped to {clusterCapacity:N0} AADT " +
              $"({attenuationFactor:P0} of {entryRoadDescription}: {totalEntryAadt:N0})";

        warnings.Add(warningMsg);

        _logger.LogDebug("Pendant cluster correction: {Road} from {Original:N0} to {Corrected:N0} (depth={Depth})",
            road.FullName, originalAadt, correctedAadt, cluster.Depth);

        return new TopologyViolation
        {
            LinearId = road.LinearId,
            RoadName = road.FullName,
            ViolationType = "PendantCluster",
            OriginalEstimate = originalAadt,
            CorrectedEstimate = correctedAadt,
            Difference = originalAadt - correctedAadt,
            PercentChange = originalAadt > 0 ? (originalAadt - correctedAadt) * 100.0 / originalAadt : 0,
            Reason = warningMsg
        };
    }

    /// <summary>
    /// Generate topology validation report
    /// </summary>
    public TopologyValidationReport GenerateReport(List<TopologyViolation> violations)
    {
        var totalRoads = _connectivityDegree.Count;
        var degrees = _connectivityDegree.Values.ToList();

        return new TopologyValidationReport
        {
            TotalRoadsAnalyzed = totalRoads,
            DeadEndRoads = _deadEndRoads.Count,
            IsolatedRoads = _isolatedRoads.Count,
            TopologyCorrectionsApplied = violations.Count,
            DeadEndViolationsFixed = violations.Count(v => v.ViolationType == "DeadEnd"),
            LowConnectivityCorrections = violations.Count(v => v.ViolationType == "LowConnectivity"),
            AverageConnectivity = degrees.Any() ? degrees.Average() : 0,
            MedianConnectivity = degrees.Any() ? degrees.OrderBy(x => x).Skip(degrees.Count / 2).First() : 0,
            TopViolations = violations.OrderByDescending(v => Math.Abs(v.Difference)).Take(20).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Calculate Haversine distance between two points in meters
    /// </summary>
    private double CalculateDistanceMeters(double lon1, double lat1, double lon2, double lat2)
    {
        const double R = 6371000; // Earth radius in meters

        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    #region Phase 1.6: Articulation Point Detection and Single-Entry Cluster Identification

    /// <summary>
    /// Find all articulation points in the road network using Tarjan's algorithm
    /// An articulation point is a vertex whose removal disconnects the graph
    /// </summary>
    private void FindArticulationPoints()
    {
        _logger.LogDebug("Finding articulation points using Tarjan's algorithm...");

        var visited = new Dictionary<string, bool>();
        var discoveryTime = new Dictionary<string, int>();
        var lowLink = new Dictionary<string, int>();
        var parent = new Dictionary<string, string?>();
        int time = 0;

        _articulationPoints.Clear();

        foreach (var node in _adjacencyGraph.Keys)
        {
            if (!visited.ContainsKey(node))
            {
                ArticulationPointDFS(node, visited, discoveryTime, lowLink, parent, ref time);
            }
        }

        _logger.LogDebug("Found {Count} articulation points in the road network", _articulationPoints.Count);

        // Debug: Check if Johnson Bend is an articulation point
        if (_articulationPoints.Contains("110416479110"))
        {
            _logger.LogWarning("✓ Johnson Bend Rd (110416479110) IS an articulation point");
        }
        else
        {
            _logger.LogWarning("⚠️ Johnson Bend Rd (110416479110) is NOT an articulation point");
        }

        // Debug: Check if Oak Dr is an articulation point
        if (_articulationPoints.Contains("110416445656"))
        {
            _logger.LogWarning("✓ Oak Dr (110416445656) IS an articulation point");
        }
        else
        {
            _logger.LogDebug("Oak Dr (110416445656) is not an articulation point (expected - it's inside a cluster)");
        }
    }

    /// <summary>
    /// DFS traversal for Tarjan's articulation point algorithm
    /// </summary>
    private void ArticulationPointDFS(
        string node,
        Dictionary<string, bool> visited,
        Dictionary<string, int> disc,
        Dictionary<string, int> low,
        Dictionary<string, string?> parent,
        ref int time)
    {
        int children = 0;
        visited[node] = true;
        disc[node] = low[node] = ++time;

        if (!_adjacencyGraph.ContainsKey(node))
            return;

        foreach (var neighbor in _adjacencyGraph[node])
        {
            if (!visited.ContainsKey(neighbor))
            {
                children++;
                parent[neighbor] = node;
                ArticulationPointDFS(neighbor, visited, disc, low, parent, ref time);

                // Update low link value
                low[node] = Math.Min(low[node], low[neighbor]);

                // Case 1: Root node with multiple children is an articulation point
                if (parent.GetValueOrDefault(node) == null && children > 1)
                {
                    _articulationPoints.Add(node);
                }

                // Case 2: Non-root node where low[neighbor] >= disc[node]
                // This means there's no back edge from neighbor's subtree to node's ancestors
                if (parent.GetValueOrDefault(node) != null && low[neighbor] >= disc[node])
                {
                    _articulationPoints.Add(node);
                }
            }
            else if (neighbor != parent.GetValueOrDefault(node))
            {
                // Back edge - update low link
                low[node] = Math.Min(low[node], disc[neighbor]);
            }
        }
    }

    /// <summary>
    /// Identify single-entry clusters from detected articulation points
    /// A single-entry cluster is a group of roads accessible only via one articulation point
    /// </summary>
    private void IdentifySingleEntryClusters(List<RoadSegment> allRoads)
    {
        _logger.LogDebug("Identifying single-entry clusters from {Count} articulation points...", _articulationPoints.Count);

        _singleEntryClusters.Clear();
        _roadToCluster.Clear();

        foreach (var articulationPoint in _articulationPoints)
        {
            // Find all components that would be disconnected if we remove this articulation point
            var components = FindComponentsWithoutVertex(articulationPoint);

            // Skip if only one component (articulation point not actually disconnecting anything)
            if (components.Count <= 1)
                continue;

            // Find the LARGEST component - this is the main network
            var mainNetworkComponent = components.OrderByDescending(c => c.Count).First();

            // All OTHER components are pendant clusters
            var pendantClusters = components.Where(c => c != mainNetworkComponent).ToList();

            foreach (var component in pendantClusters)
            {
                // Check if this component truly only connects through the articulation point
                if (IsTruePendantClusterForVertex(component, articulationPoint))
                {
                    if (!_singleEntryClusters.ContainsKey(articulationPoint))
                    {
                        _singleEntryClusters[articulationPoint] = new HashSet<string>();
                    }

                    // Add all cluster roads
                    foreach (var clusterRoad in component)
                    {
                        _singleEntryClusters[articulationPoint].Add(clusterRoad);
                        _roadToCluster[clusterRoad] = articulationPoint;
                    }

                    _logger.LogDebug("Single-entry cluster identified: Entry={Entry}, Size={Size}",
                        articulationPoint, component.Count);
                }
            }
        }

        _logger.LogInformation("  ✓ Identified {Count} single-entry clusters with {TotalRoads} total roads",
            _singleEntryClusters.Count,
            _singleEntryClusters.Sum(kvp => kvp.Value.Count));

        // Debug: Check if Oak Dr is in a cluster
        if (_roadToCluster.ContainsKey("110416445656"))
        {
            var entryRoad = _roadToCluster["110416445656"];
            var clusterSize = _singleEntryClusters[entryRoad].Count;
            _logger.LogWarning("✓ Oak Dr (110416445656) is in a cluster with entry road {EntryRoad}, cluster size: {Size}",
                entryRoad, clusterSize);
        }
        else
        {
            _logger.LogWarning("⚠️ Oak Dr (110416445656) is NOT in any cluster");
        }
    }

    /// <summary>
    /// Find all connected components when a vertex is removed from the graph
    /// Returns a list of components (each component is a set of nodes)
    /// </summary>
    private List<HashSet<string>> FindComponentsWithoutVertex(string excludeVertex)
    {
        var components = new List<HashSet<string>>();
        var visited = new HashSet<string>();
        visited.Add(excludeVertex); // Exclude the articulation point

        foreach (var node in _adjacencyGraph.Keys)
        {
            if (visited.Contains(node))
                continue;

            // Start BFS from this unvisited node
            var component = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(node);
            component.Add(node);
            visited.Add(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (!_adjacencyGraph.ContainsKey(current))
                    continue;

                foreach (var neighbor in _adjacencyGraph[current])
                {
                    // Skip the excluded vertex
                    if (neighbor == excludeVertex)
                        continue;

                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count > 0)
            {
                components.Add(component);
            }
        }

        return components;
    }

    /// <summary>
    /// Check if a cluster is truly a pendant cluster (only accessible via one articulation point)
    /// </summary>
    private bool IsTruePendantClusterForVertex(HashSet<string> clusterRoads, string articulationPoint)
    {
        // Check if any road in the cluster connects to a road outside the cluster
        // (other than the articulation point)
        foreach (var clusterRoad in clusterRoads)
        {
            if (!_adjacencyGraph.ContainsKey(clusterRoad))
                continue;

            foreach (var neighbor in _adjacencyGraph[clusterRoad])
            {
                if (!clusterRoads.Contains(neighbor) && neighbor != articulationPoint)
                {
                    // Found a connection to main network that isn't the articulation point
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Step 0: Build primal graph with intersections as vertices
    /// </summary>
    private PrimalGraph BuildPrimalGraph(List<RoadSegment> allRoads)
    {
        _logger.LogDebug("Building primal graph (intersections as vertices, segments as edges)...");

        var graph = new PrimalGraph();

        // Step 1: Collect ALL coordinates from all segments and count occurrences
        _logger.LogDebug("Step 1: Collecting all coordinates from all segments...");
        var coordinateCounts = new Dictionary<string, (double lat, double lon, int count)>();
        var segmentCoordinates = new Dictionary<string, List<List<double>>>();

        foreach (var road in allRoads)
        {
            if (_duplicateRoads.Contains(road.LinearId))
                continue;

            var coords = GetCoordinates(road);
            if (coords.Count < 2)
            {
                _logger.LogWarning("Road {LinearId} has insufficient coordinates ({Count}), skipping",
                    road.LinearId, coords.Count);
                continue;
            }

            segmentCoordinates[road.LinearId] = coords;

            // Count occurrences of each coordinate across ALL segments
            foreach (var coord in coords)
            {
                var lat = coord[1];
                var lon = coord[0];
                var coordId = $"Int_{lat:F6}_{lon:F6}";

                if (coordinateCounts.ContainsKey(coordId))
                {
                    coordinateCounts[coordId] = (lat, lon, coordinateCounts[coordId].count + 1);
                }
                else
                {
                    coordinateCounts[coordId] = (lat, lon, 1);
                }
            }
        }

        // Step 2: Identify intersection points (coordinates shared by 2+ segments OR segment endpoints)
        _logger.LogDebug("Step 2: Identifying intersection points (shared coordinates or endpoints)...");
        var intersectionPoints = new HashSet<string>();

        // Add all coordinates that appear in 2+ segments
        foreach (var (coordId, (lat, lon, count)) in coordinateCounts)
        {
            if (count >= 2)
            {
                intersectionPoints.Add(coordId);
            }
        }

        // Also add all segment endpoints (even if they only appear once) to preserve network endpoints
        foreach (var coords in segmentCoordinates.Values)
        {
            var startId = $"Int_{coords.First()[1]:F6}_{coords.First()[0]:F6}";
            var endId = $"Int_{coords.Last()[1]:F6}_{coords.Last()[0]:F6}";
            intersectionPoints.Add(startId);
            intersectionPoints.Add(endId);
        }

        _logger.LogInformation("  Found {Intersections} intersection points from {Total} total coordinates",
            intersectionPoints.Count, coordinateCounts.Count);

        // Step 3: Create intersection vertices for all intersection points
        _logger.LogDebug("Step 3: Creating intersection vertices...");
        foreach (var intersectionId in intersectionPoints)
        {
            var (lat, lon, _) = coordinateCounts[intersectionId];

            graph.Intersections[intersectionId] = new Intersection
            {
                Id = intersectionId,
                Latitude = lat,
                Longitude = lon,
                ConnectedSegments = new List<string>(),
                ConnectedSplitSegments = new List<string>()
            };
            graph.AdjacencyGraph[intersectionId] = new List<string>();
            graph.IntersectionToSegments[intersectionId] = new List<string>();
            graph.IntersectionToSplitSegments[intersectionId] = new List<string>();
        }

        // Step 4: For each segment, split it at intersections and create split segment records
        _logger.LogDebug("Step 4: Splitting segments at intersections and creating split records...");
        int splitCount = 0;
        int skippedCount = 0;
        int totalSplitsCreated = 0;

        foreach (var (linearId, coords) in segmentCoordinates)
        {
            // Find which coordinate indices are intersections
            var intersectionIndices = new List<int>();
            var allIntersectionsOnSegment = new List<string>();

            for (int i = 0; i < coords.Count; i++)
            {
                var coordId = $"Int_{coords[i][1]:F6}_{coords[i][0]:F6}";
                if (intersectionPoints.Contains(coordId))
                {
                    intersectionIndices.Add(i);
                    allIntersectionsOnSegment.Add(coordId);
                }
            }

            // Segment must have at least 2 intersection points (start and end at minimum)
            if (intersectionIndices.Count < 2)
            {
                _logger.LogDebug("Segment {LinearId} has insufficient intersections ({Count}), skipping",
                    linearId, intersectionIndices.Count);
                skippedCount++;
                continue;
            }

            // If segment passes through multiple intersections, we split it
            if (intersectionIndices.Count > 2)
            {
                splitCount++;
            }

            // Initialize parent tracking
            if (!graph.ParentToSplits.ContainsKey(linearId))
            {
                graph.ParentToSplits[linearId] = new List<string>();
            }

            // Store overall segment start/end for legacy compatibility
            var segmentStartId = $"Int_{coords[intersectionIndices[0]][1]:F6}_{coords[intersectionIndices[0]][0]:F6}";
            var segmentEndId = $"Int_{coords[intersectionIndices[^1]][1]:F6}_{coords[intersectionIndices[^1]][0]:F6}";
            graph.SegmentToIntersections[linearId] = (segmentStartId, segmentEndId);
            graph.SegmentToAllIntersections[linearId] = allIntersectionsOnSegment;

            // Legacy: Add parent segment to start/end intersections
            graph.IntersectionToSegments[segmentStartId].Add(linearId);
            graph.IntersectionToSegments[segmentEndId].Add(linearId);
            graph.Intersections[segmentStartId].ConnectedSegments.Add(linearId);
            if (segmentStartId != segmentEndId)
            {
                graph.Intersections[segmentEndId].ConnectedSegments.Add(linearId);
            }

            // Create split segments between each consecutive pair of intersections
            double totalLength = 0;
            for (int i = 0; i < intersectionIndices.Count - 1; i++)
            {
                var startIdx = intersectionIndices[i];
                var endIdx = intersectionIndices[i + 1];

                var startId = $"Int_{coords[startIdx][1]:F6}_{coords[startIdx][0]:F6}";
                var endId = $"Int_{coords[endIdx][1]:F6}_{coords[endIdx][0]:F6}";

                // Calculate approximate length of this split (sum of coordinate-to-coordinate distances)
                double splitLength = 0;
                for (int j = startIdx; j < endIdx; j++)
                {
                    var dist = CalculateDistanceMeters(
                        coords[j][0], coords[j][1],
                        coords[j + 1][0], coords[j + 1][1]);
                    splitLength += dist;
                }
                totalLength += splitLength;

                // Create split segment record
                var splitId = $"{linearId}_Split_{i}";
                var split = new SegmentSplit
                {
                    SplitId = splitId,
                    ParentLinearId = linearId,
                    StartIntersectionId = startId,
                    EndIntersectionId = endId,
                    StartCoordIndex = startIdx,
                    EndCoordIndex = endIdx,
                    LengthFraction = 0  // Will calculate after we know total length
                };

                graph.SplitSegments[splitId] = split;
                graph.ParentToSplits[linearId].Add(splitId);

                // Associate split with both intersections
                graph.IntersectionToSplitSegments[startId].Add(splitId);
                graph.IntersectionToSplitSegments[endId].Add(splitId);
                graph.Intersections[startId].ConnectedSplitSegments.Add(splitId);
                graph.Intersections[endId].ConnectedSplitSegments.Add(splitId);

                // Build adjacency between consecutive intersections
                if (!graph.AdjacencyGraph[startId].Contains(endId))
                {
                    graph.AdjacencyGraph[startId].Add(endId);
                }
                if (!graph.AdjacencyGraph[endId].Contains(startId))
                {
                    graph.AdjacencyGraph[endId].Add(startId);
                }

                totalSplitsCreated++;
            }

            // Update length fractions now that we know total length
            if (totalLength > 0)
            {
                foreach (var splitId in graph.ParentToSplits[linearId])
                {
                    var split = graph.SplitSegments[splitId];
                    double splitLength = 0;
                    for (int j = split.StartCoordIndex; j < split.EndCoordIndex; j++)
                    {
                        var dist = CalculateDistanceMeters(
                            coords[j][0], coords[j][1],
                            coords[j + 1][0], coords[j + 1][1]);
                        splitLength += dist;
                    }
                    split.LengthFraction = splitLength / totalLength;
                }
            }
        }

        _logger.LogInformation("  ✓ Built primal graph: {Intersections} intersections, {Segments} parent segments, {Splits} split segments",
            graph.Intersections.Count, graph.SegmentToIntersections.Count, totalSplitsCreated);
        _logger.LogInformation("  Parent segments requiring splits: {Split}", splitCount);
        _logger.LogInformation("  Segments skipped (insufficient intersections): {Skipped}", skippedCount);

        // DEBUG: Dump graph structure to file
        DumpGraphToFile(graph, allRoads);

        return graph;
    }

    /// <summary>
    /// Dump graph structure to file for debugging
    /// </summary>
    private void DumpGraphToFile(PrimalGraph graph, List<RoadSegment> allRoads)
    {
        // Debug artifact only — written relative to the current directory, and a failure
        // to write it must never kill the pipeline.
        var outputPath = "primal-graph-debug.txt";

        try
        {
            using var writer = new StreamWriter(outputPath);
            writer.WriteLine("=".PadRight(100, '='));
            writer.WriteLine("PRIMAL GRAPH DEBUG DUMP");
            writer.WriteLine("=".PadRight(100, '='));
            writer.WriteLine();

            // 1. Dump all intersections with split segment info
            writer.WriteLine($"INTERSECTIONS: {graph.Intersections.Count} total");
            writer.WriteLine("-".PadRight(100, '-'));

            // Show first 50 intersections with detailed split info
            var intersectionsToShow = graph.Intersections.OrderBy(x => x.Key).Take(50).ToList();
            foreach (var (id, intersection) in intersectionsToShow)
            {
                writer.WriteLine($"ID: {id}");
                writer.WriteLine($"  Lat: {intersection.Latitude:F8}, Lon: {intersection.Longitude:F8}");
                writer.WriteLine($"  Connected parent segments (legacy): {intersection.ConnectedSegments.Count}");
                writer.WriteLine($"  Connected split segments: {intersection.ConnectedSplitSegments.Count}");
                writer.WriteLine($"  Adjacent intersections: {graph.AdjacencyGraph[id].Count}");

                // Show split segment details if count is reasonable
                if (intersection.ConnectedSplitSegments.Count > 0 && intersection.ConnectedSplitSegments.Count <= 10)
                {
                    writer.WriteLine($"  Split segment details:");
                    foreach (var splitId in intersection.ConnectedSplitSegments.Take(5))
                    {
                        var split = graph.SplitSegments[splitId];
                        writer.WriteLine($"    - {splitId}: {split.ParentLinearId} ({split.StartIntersectionId} -> {split.EndIntersectionId})");
                    }
                }
                writer.WriteLine();
            }

            if (graph.Intersections.Count > 50)
            {
                writer.WriteLine($"... and {graph.Intersections.Count - 50} more intersections");
                writer.WriteLine();
            }

            writer.WriteLine();
            writer.WriteLine("=".PadRight(100, '='));
            writer.WriteLine($"SPLIT SEGMENTS: {graph.SplitSegments.Count} total");
            writer.WriteLine("-".PadRight(100, '-'));
            writer.WriteLine($"Parent segments with splits: {graph.ParentToSplits.Count}");
            writer.WriteLine($"Average splits per parent: {(graph.ParentToSplits.Count > 0 ? (double)graph.SplitSegments.Count / graph.ParentToSplits.Count : 0):F2}");
            writer.WriteLine();

            // Show distribution of splits per parent
            var splitDistribution = graph.ParentToSplits
                .GroupBy(kvp => kvp.Value.Count)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());

            writer.WriteLine("Split distribution:");
            foreach (var (splitCount, parentCount) in splitDistribution.OrderByDescending(kvp => kvp.Value).Take(10))
            {
                writer.WriteLine($"  {splitCount} split(s): {parentCount} parent segments");
            }
            writer.WriteLine();

            writer.WriteLine("=".PadRight(100, '='));
            writer.WriteLine($"PARENT SEGMENTS: {graph.SegmentToIntersections.Count} total");
            writer.WriteLine("-".PadRight(100, '-'));

            // 2. Dump all segments with their endpoint coordinates
            foreach (var road in allRoads.Where(r => !_duplicateRoads.Contains(r.LinearId)))
            {
                if (!graph.SegmentToIntersections.ContainsKey(road.LinearId))
                    continue;

                var coords = GetCoordinates(road);
                if (coords.Count < 2)
                    continue;

                var (startId, endId) = graph.SegmentToIntersections[road.LinearId];
                var startCoord = coords.First();
                var endCoord = coords.Last();

                writer.WriteLine($"Segment: {road.LinearId} - {road.FullName}");
                writer.WriteLine($"  Start: [{startCoord[1]:F8}, {startCoord[0]:F8}] -> Intersection: {startId}");
                writer.WriteLine($"  End:   [{endCoord[1]:F8}, {endCoord[0]:F8}] -> Intersection: {endId}");
                writer.WriteLine($"  Total coordinates in geometry: {coords.Count}");
                writer.WriteLine();
            }

            writer.WriteLine();
            writer.WriteLine("=".PadRight(100, '='));
            writer.WriteLine("CONNECTIVITY ANALYSIS");
            writer.WriteLine("-".PadRight(100, '-'));

            // 3. Find connected components
            var components = FindConnectedComponentsInPrimalGraph(graph);
            writer.WriteLine($"Connected components: {components.Count}");
            writer.WriteLine();

            for (int i = 0; i < Math.Min(10, components.Count); i++)
            {
                var component = components[i];
                writer.WriteLine($"Component {i + 1}: {component.Count} intersections");

                var segmentsInComponent = graph.SegmentToIntersections
                    .Where(kvp => component.Contains(kvp.Value.startId) && component.Contains(kvp.Value.endId))
                    .Count();

                writer.WriteLine($"  Segments: {segmentsInComponent}");
                writer.WriteLine($"  Sample intersections: {string.Join(", ", component.Take(5))}");
                writer.WriteLine();
            }

            if (components.Count > 10)
            {
                writer.WriteLine($"... and {components.Count - 10} more components");
            }

            _logger.LogWarning("⚠️  Graph debug dump written to: {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write graph debug dump to {Path}; continuing", outputPath);
        }
    }

    /// <summary>
    /// Find connected components in primal graph
    /// </summary>
    private List<HashSet<string>> FindConnectedComponentsInPrimalGraph(PrimalGraph graph)
    {
        var components = new List<HashSet<string>>();
        var visited = new HashSet<string>();

        foreach (var intersectionId in graph.Intersections.Keys)
        {
            if (visited.Contains(intersectionId))
                continue;

            // BFS to find connected component
            var component = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(intersectionId);
            component.Add(intersectionId);
            visited.Add(intersectionId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var neighbor in graph.AdjacencyGraph[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components.OrderByDescending(c => c.Count).ToList();
    }

    /// <summary>
    /// Create or get intersection ID from coordinates
    /// </summary>
    private string CreateOrGetIntersection(List<double> coord, PrimalGraph graph)
    {
        // Create intersection ID from coordinates (rounded to avoid floating point issues)
        // Format: Int_{latitude:F6}_{longitude:F6}
        var id = $"Int_{coord[1]:F6}_{coord[0]:F6}";  // lat_lon

        if (!graph.Intersections.ContainsKey(id))
        {
            graph.Intersections[id] = new Intersection
            {
                Id = id,
                Latitude = coord[1],
                Longitude = coord[0],
                ConnectedSegments = new List<string>()
            };
            graph.AdjacencyGraph[id] = new List<string>();
            graph.IntersectionToSegments[id] = new List<string>();
        }

        return id;
    }

    /// <summary>
    /// Step 1: Find biconnected components using Tarjan's algorithm
    /// </summary>
    private (HashSet<string> articulationPoints, List<BiconnectedComponent> components)
        FindBiconnectedComponentsOnPrimalGraph(PrimalGraph graph)
    {
        _logger.LogDebug("Finding biconnected components on primal graph (intersections as vertices)...");

        var visited = new Dictionary<string, bool>();
        var disc = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var parent = new Dictionary<string, string?>();
        var articulationPoints = new HashSet<string>();
        var componentIntersections = new List<HashSet<string>>();
        var edgeStack = new Stack<(string, string)>();
        int time = 0;

        // Run DFS from each unvisited intersection
        foreach (var intersectionId in graph.Intersections.Keys)
        {
            if (!visited.ContainsKey(intersectionId))
            {
                BiconnectedDFS(intersectionId, graph.AdjacencyGraph, visited, disc, low,
                              parent, articulationPoints, componentIntersections,
                              edgeStack, ref time);
            }
        }

        // Convert intersection sets to BiconnectedComponent objects with split segment mappings
        var components = new List<BiconnectedComponent>();
        int componentId = 0;
        foreach (var intersectionSet in componentIntersections)
        {
            var component = new BiconnectedComponent
            {
                Id = componentId++,
                Intersections = intersectionSet,
                SplitSegments = new List<string>()
            };

            // Find all SPLIT segments where BOTH endpoints are in this component
            // This is critical: a split belongs to a component only if it's fully contained
            foreach (var (splitId, split) in graph.SplitSegments)
            {
                if (intersectionSet.Contains(split.StartIntersectionId) &&
                    intersectionSet.Contains(split.EndIntersectionId))
                {
                    component.SplitSegments.Add(splitId);
                }
            }

            components.Add(component);
        }

        _logger.LogInformation("  ✓ Found {ArticulationPoints} articulation points and {Components} biconnected components",
            articulationPoints.Count, components.Count);

        return (articulationPoints, components);
    }

    /// <summary>
    /// DFS for Tarjan's biconnected components algorithm
    /// </summary>
    private void BiconnectedDFS(
        string intersection,
        Dictionary<string, List<string>> adjacencyGraph,
        Dictionary<string, bool> visited,
        Dictionary<string, int> disc,
        Dictionary<string, int> low,
        Dictionary<string, string?> parent,
        HashSet<string> articulationPoints,
        List<HashSet<string>> components,
        Stack<(string, string)> edgeStack,
        ref int time)
    {
        int children = 0;
        visited[intersection] = true;
        disc[intersection] = low[intersection] = ++time;

        if (!adjacencyGraph.ContainsKey(intersection))
            return;

        foreach (var neighbor in adjacencyGraph[intersection])
        {
            if (!visited.ContainsKey(neighbor))
            {
                children++;
                parent[neighbor] = intersection;
                edgeStack.Push((intersection, neighbor));

                BiconnectedDFS(neighbor, adjacencyGraph, visited, disc, low, parent,
                              articulationPoints, components, edgeStack, ref time);

                low[intersection] = Math.Min(low[intersection], low[neighbor]);

                // Check articulation point conditions
                bool isArticulationPoint = false;

                // Case 1: Root with multiple children
                if (!parent.ContainsKey(intersection) && children > 1)
                {
                    isArticulationPoint = true;
                }

                // Case 2: Non-root where child subtree cannot reach ancestors
                if (parent.ContainsKey(intersection) && parent[intersection] != null && low[neighbor] >= disc[intersection])
                {
                    isArticulationPoint = true;
                }

                if (isArticulationPoint)
                {
                    articulationPoints.Add(intersection);

                    // Pop edges to form biconnected component
                    var component = new HashSet<string>();
                    (string from, string to) edge;
                    do
                    {
                        edge = edgeStack.Pop();
                        component.Add(edge.from);
                        component.Add(edge.to);
                    } while (edgeStack.Count > 0 && (edge.from != intersection || edge.to != neighbor));

                    components.Add(component);
                }
            }
            else if (neighbor != parent.GetValueOrDefault(intersection) &&
                     disc[neighbor] < disc[intersection])
            {
                // Back edge
                edgeStack.Push((intersection, neighbor));
                low[intersection] = Math.Min(low[intersection], disc[neighbor]);
            }
        }
    }

    /// <summary>
    /// Step 2: Build block-cut tree from biconnected components
    /// </summary>
    private BlockCutTree BuildBlockCutTree(
        PrimalGraph primalGraph,
        HashSet<string> articulationPoints,
        List<BiconnectedComponent> biconnectedComponents)
    {
        _logger.LogDebug("Building block-cut tree from {Components} biconnected components...", biconnectedComponents.Count);

        var tree = new BlockCutTree();

        // Create cut nodes for each articulation point (intersection)
        foreach (var intersectionId in articulationPoints)
        {
            tree.CutNodes[intersectionId] = new TreeNode
            {
                Type = TreeNode.NodeType.CutVertex,
                Id = intersectionId,
                Children = new List<string>(),
                Parent = null,
                Depth = 0
            };
        }

        // Create block nodes for each biconnected component
        foreach (var component in biconnectedComponents)
        {
            var blockId = component.Id;
            var blockNodeId = $"Block_{blockId}";

            tree.BlockNodes[blockId] = new TreeNode
            {
                Type = TreeNode.NodeType.Block,
                Id = blockNodeId,
                Children = new List<string>(),
                Parent = null,
                Depth = 0
            };

            // Map SPLIT segments to this block
            tree.SplitSegments[blockNodeId] = component.SplitSegments;

            // Connect block to its articulation points
            foreach (var intersectionId in component.Intersections)
            {
                if (articulationPoints.Contains(intersectionId))
                {
                    // Add edge between block node and cut node
                    tree.BlockNodes[blockId].Children.Add(intersectionId);
                    tree.CutNodes[intersectionId].Children.Add(blockNodeId);
                }
            }
        }

        // Compute tree structure (parent-child relationships and depths)
        ComputeTreeStructure(tree);

        _logger.LogInformation("  ✓ Built block-cut tree with {Blocks} blocks and {Cuts} cut vertices",
            tree.BlockNodes.Count, tree.CutNodes.Count);

        return tree;
    }

    /// <summary>
    /// Compute parent-child relationships and depths in the block-cut tree
    /// </summary>
    private void ComputeTreeStructure(BlockCutTree tree)
    {
        // Find root node(s) - blocks with highest connectivity or pick the largest block
        var rootBlockId = tree.BlockNodes.Keys
            .OrderByDescending(id => tree.SplitSegments[$"Block_{id}"].Count)
            .First();

        var rootNode = tree.BlockNodes[rootBlockId];

        _logger.LogDebug("Selected root block: {RootId} with {Segments} split segments",
            rootNode.Id, tree.SplitSegments[rootNode.Id].Count);

        // BFS to compute depths and parent relationships
        var visited = new HashSet<string> { rootNode.Id };
        var queue = new Queue<(TreeNode node, int depth)>();
        queue.Enqueue((rootNode, 0));

        int nodesVisited = 0;
        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            node.Depth = depth;
            nodesVisited++;

            foreach (var childId in node.Children)
            {
                if (visited.Contains(childId))
                    continue;

                visited.Add(childId);

                TreeNode childNode;
                if (childId.StartsWith("Block_"))
                {
                    var blockId = int.Parse(childId.Replace("Block_", ""));
                    childNode = tree.BlockNodes[blockId];
                }
                else
                {
                    childNode = tree.CutNodes[childId];
                }

                childNode.Parent = node.Id;
                queue.Enqueue((childNode, depth + 1));
            }
        }

        var blocksWithParents = tree.BlockNodes.Values.Count(b => b.Parent != null);
        var cutsWithParents = tree.CutNodes.Values.Count(c => c.Parent != null);

        _logger.LogWarning("⚠️  Tree structure incomplete!");
        _logger.LogWarning("  Nodes visited by BFS: {Visited} out of {Total} total nodes",
            nodesVisited, tree.BlockNodes.Count + tree.CutNodes.Count);
        _logger.LogWarning("  Blocks with parents: {BlocksWithParents} out of {TotalBlocks}",
            blocksWithParents, tree.BlockNodes.Count);
        _logger.LogWarning("  Cuts with parents: {CutsWithParents} out of {TotalCuts}",
            cutsWithParents, tree.CutNodes.Count);
    }

    /// <summary>
    /// Step 3: Identify pendant clusters (leaf blocks with single entry points)
    /// </summary>
    private List<PendantCluster> IdentifyPendantClusters(
        BlockCutTree tree,
        PrimalGraph primalGraph)
    {
        _logger.LogDebug("Identifying pendant clusters from block-cut tree...");

        var clusters = new List<PendantCluster>();

        // Find leaf blocks in the tree (blocks with no block children)
        var leafBlocks = tree.BlockNodes.Values
            .Where(block => IsLeafBlock(block, tree))
            .ToList();

        _logger.LogDebug("Found {LeafBlocks} leaf blocks out of {TotalBlocks} total blocks",
            leafBlocks.Count, tree.BlockNodes.Count);

        int analyzedCount = 0;
        int createdCount = 0;
        foreach (var leafBlock in leafBlocks)
        {
            analyzedCount++;
            bool isVerboseDebug = createdCount < 3; // Verbose for first 3 CREATED clusters

            if (isVerboseDebug)
            {
                _logger.LogDebug("=== Analyzing leaf block {BlockId} (analyzed #{Analyzed}, cluster #{Created}) ===",
                    leafBlock.Id, analyzedCount, createdCount + 1);
                _logger.LogDebug("  Block depth: {Depth}", leafBlock.Depth);
                _logger.LogDebug("  Block parent: {Parent}", leafBlock.Parent ?? "null");
                _logger.LogDebug("  Block children: {Children}", string.Join(", ", leafBlock.Children));
            }

            // The parent cut node (articulation point) is the entry intersection
            if (leafBlock.Parent == null)
            {
                if (isVerboseDebug)
                    _logger.LogDebug("  SKIP: Root block (no parent)");
                continue; // Root block, not a pendant cluster
            }

            var parentCutNode = tree.CutNodes.GetValueOrDefault(leafBlock.Parent);
            if (parentCutNode == null)
            {
                if (isVerboseDebug)
                    _logger.LogDebug("  SKIP: Parent cut node not found");
                continue;
            }

            var entryIntersectionId = parentCutNode.Id;

            if (isVerboseDebug)
            {
                _logger.LogDebug("  Entry intersection (parent cut node): {EntryId}", entryIntersectionId);
            }

            // Get all SPLIT segments in this leaf block (not parent IDs!)
            var clusterSplitIds = new HashSet<string>(tree.SplitSegments[leafBlock.Id]);

            if (isVerboseDebug)
            {
                _logger.LogDebug("  Initial leaf block split segments: {Count}", clusterSplitIds.Count);
                _logger.LogDebug("    First 3 splits: {Splits}",
                    string.Join(", ", clusterSplitIds.Take(3)));
            }

            // IMPORTANT: Leaf blocks are by definition leaves - they have no descendant blocks
            // The old recursive logic was incorrectly following bidirectional edges in the block-cut tree
            // For now, we only use the leaf block's own segments
            // TODO: If we need nested subdivision support, implement proper downward-only traversal

            if (isVerboseDebug)
            {
                _logger.LogDebug("  Cluster segments (leaf block only): {Count}", clusterSplitIds.Count);
            }

            // Get all SPLIT segments at the entry intersection
            var allSplitsAtEntry = primalGraph.IntersectionToSplitSegments.GetValueOrDefault(entryIntersectionId, new List<string>());

            if (isVerboseDebug)
            {
                _logger.LogDebug("  Split segments at entry intersection: {Count}", allSplitsAtEntry.Count);
                _logger.LogDebug("    All entry intersection splits: {Splits}",
                    string.Join(", ", allSplitsAtEntry.Take(5)));
            }

            // CRITICAL FIX: Filter at SPLIT level, not parent level
            // Entry splits are those at the entry intersection that are NOT in the cluster
            var entrySplitIds = allSplitsAtEntry
                .Where(splitId => !clusterSplitIds.Contains(splitId))
                .ToList();

            // Now convert to parent IDs for AADT lookup
            var entryParentIds = entrySplitIds
                .Select(splitId => primalGraph.SplitSegments[splitId].ParentLinearId)
                .Distinct()
                .ToList();

            // Also derive parent IDs for cluster segments (for constraint checking later)
            var clusterParentIds = clusterSplitIds
                .Select(splitId => primalGraph.SplitSegments[splitId].ParentLinearId)
                .Distinct()
                .ToHashSet();

            if (isVerboseDebug)
            {
                _logger.LogDebug("  Entry split segments (from main network): {Count}", entrySplitIds.Count);
                _logger.LogDebug("  Unique entry parent segments: {Count}", entryParentIds.Count);
                if (entryParentIds.Any())
                {
                    _logger.LogDebug("    Entry parent segments: {Segments}", string.Join(", ", entryParentIds));
                }
                else
                {
                    _logger.LogWarning("  ⚠️ NO ENTRY SEGMENTS! All splits at entry intersection are in cluster.");
                    _logger.LogDebug("    Cluster split segments: {Count}", clusterSplitIds.Count);
                }
            }

            var cluster = new PendantCluster
            {
                EntryIntersectionId = entryIntersectionId,
                EntrySplitSegments = entrySplitIds,
                EntryParentSegments = entryParentIds,
                ClusterSplitSegments = clusterSplitIds,
                ClusterParentSegments = clusterParentIds,
                Depth = leafBlock.Depth
            };

            clusters.Add(cluster);
            createdCount++; // Increment after creating cluster

            if (!isVerboseDebug)
            {
                _logger.LogDebug("Pendant cluster: Entry={Entry}, Depth={Depth}, ClusterSplits={ClusterSplitCount}, ClusterParents={ClusterParentCount}, EntrySplits={EntrySplitCount}, EntryParents={EntryParentCount}",
                    entryIntersectionId, leafBlock.Depth, clusterSplitIds.Count, clusterParentIds.Count, entrySplitIds.Count, entryParentIds.Count);
            }
        }

        _logger.LogInformation("  ✓ Identified {Clusters} pendant clusters", clusters.Count);

        return clusters;
    }

    /// <summary>
    /// Check if a block node is a leaf (no block children)
    /// </summary>
    private bool IsLeafBlock(TreeNode block, BlockCutTree tree)
    {
        // A leaf block has no block children (may have cut vertex children, but they don't count)
        foreach (var childId in block.Children)
        {
            if (childId.StartsWith("Block_"))
            {
                return false; // Has a block child, not a leaf
            }
        }
        return true;
    }

    /// <summary>
    /// Recursively add SPLIT segments from child blocks (for nested subdivisions)
    /// </summary>
    private void AddChildBlockSplitSegments(
        BlockCutTree tree,
        TreeNode block,
        HashSet<string> clusterSplitIds,
        HashSet<string> visitedNodes)
    {
        foreach (var childId in block.Children)
        {
            // Skip if already visited (prevents infinite recursion)
            if (visitedNodes.Contains(childId))
                continue;

            visitedNodes.Add(childId);
            TreeNode? childNode = null;

            if (childId.StartsWith("Block_"))
            {
                var blockId = int.Parse(childId.Replace("Block_", ""));
                childNode = tree.BlockNodes.GetValueOrDefault(blockId);

                if (childNode != null && tree.SplitSegments.ContainsKey(childNode.Id))
                {
                    clusterSplitIds.UnionWith(tree.SplitSegments[childNode.Id]);
                    AddChildBlockSplitSegments(tree, childNode, clusterSplitIds, visitedNodes);
                }
            }
            else
            {
                // It's a cut vertex - traverse through it to find block children
                childNode = tree.CutNodes.GetValueOrDefault(childId);
                if (childNode != null)
                {
                    foreach (var grandchildId in childNode.Children)
                    {
                        // Skip if already visited
                        if (visitedNodes.Contains(grandchildId))
                            continue;

                        if (grandchildId.StartsWith("Block_"))
                        {
                            visitedNodes.Add(grandchildId);
                            var blockId = int.Parse(grandchildId.Replace("Block_", ""));
                            var grandchildBlock = tree.BlockNodes.GetValueOrDefault(blockId);

                            if (grandchildBlock != null && tree.SplitSegments.ContainsKey(grandchildBlock.Id))
                            {
                                clusterSplitIds.UnionWith(tree.SplitSegments[grandchildBlock.Id]);
                                AddChildBlockSplitSegments(tree, grandchildBlock, clusterSplitIds, visitedNodes);
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion

    /// <summary>
    /// Simple geographic point class
    /// </summary>
    private class GeoPoint
    {
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public string LinearId { get; set; }

        public GeoPoint(double longitude, double latitude, string linearId)
        {
            Longitude = longitude;
            Latitude = latitude;
            LinearId = linearId;
        }
    }

    #region Phase 1.6: Primal Graph Data Structures

    /// <summary>
    /// Configuration for topology constraints
    /// </summary>
    private class TopologyConstraintConfig
    {
        /// <summary>
        /// Flow constraint ratio applied at each articulation point.
        /// Default: 0.8 (80%) accounts for bidirectional flow and capacity margin.
        /// </summary>
        public double FlowConstraintRatio { get; set; } = 0.8;

        /// <summary>
        /// Whether to apply exponential attenuation for nested clusters.
        /// When true, capacity = entryAadt × (FlowConstraintRatio ^ depth)
        /// </summary>
        public bool UseNestedAttenuation { get; set; } = true;
    }

    /// <summary>
    /// Represents a segment split between two consecutive intersections along a polyline.
    /// Each split is a logical edge in the primal graph.
    /// </summary>
    private class SegmentSplit
    {
        /// <summary>
        /// Unique identifier for this split segment
        /// Format: {ParentLinearId}_Split_{StartIntersectionId}_to_{EndIntersectionId}
        /// </summary>
        public string SplitId { get; set; } = "";

        /// <summary>
        /// Parent road segment's LinearId
        /// </summary>
        public string ParentLinearId { get; set; } = "";

        /// <summary>
        /// Starting intersection ID
        /// </summary>
        public string StartIntersectionId { get; set; } = "";

        /// <summary>
        /// Ending intersection ID
        /// </summary>
        public string EndIntersectionId { get; set; } = "";

        /// <summary>
        /// Approximate length fraction of parent segment (0.0 - 1.0)
        /// Used for proportional AADT distribution if needed
        /// </summary>
        public double LengthFraction { get; set; }

        /// <summary>
        /// Index of start coordinate in parent geometry
        /// </summary>
        public int StartCoordIndex { get; set; }

        /// <summary>
        /// Index of end coordinate in parent geometry
        /// </summary>
        public int EndCoordIndex { get; set; }
    }

    /// <summary>
    /// Represents an intersection point where roads meet
    /// </summary>
    private class Intersection
    {
        public string Id { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        /// <summary>
        /// Split segment IDs connected to this intersection
        /// </summary>
        public List<string> ConnectedSplitSegments { get; set; } = new();

        /// <summary>
        /// Parent segment IDs (original linearIds) for backward compatibility
        /// </summary>
        public List<string> ConnectedSegments { get; set; } = new();
    }

    /// <summary>
    /// Primal graph with intersections as vertices and segments as edges
    /// </summary>
    private class PrimalGraph
    {
        // Vertices = intersections
        public Dictionary<string, Intersection> Intersections { get; set; } = new();

        // Edges = road segments (intersection -> intersection)
        public Dictionary<string, List<string>> AdjacencyGraph { get; set; } = new();

        // Split segments - the core data structure for accurate topology
        public Dictionary<string, SegmentSplit> SplitSegments { get; set; } = new();

        // Mapping: parent linearId -> list of split segment IDs
        public Dictionary<string, List<string>> ParentToSplits { get; set; } = new();

        // Mapping: intersection ID -> list of split segment IDs connected to it
        public Dictionary<string, List<string>> IntersectionToSplitSegments { get; set; } = new();

        // Legacy mappings (for backward compatibility)
        public Dictionary<string, (string startId, string endId)> SegmentToIntersections { get; set; } = new();
        public Dictionary<string, List<string>> IntersectionToSegments { get; set; } = new();
        public Dictionary<string, List<string>> SegmentToAllIntersections { get; set; } = new();
    }

    /// <summary>
    /// A biconnected component (set of intersections and split segments)
    /// </summary>
    private class BiconnectedComponent
    {
        public int Id { get; set; }
        public HashSet<string> Intersections { get; set; } = new();

        /// <summary>
        /// Split segment IDs that belong to this component (both endpoints in component)
        /// </summary>
        public List<string> SplitSegments { get; set; } = new();

        /// <summary>
        /// Parent segment IDs (derived from splits) - for reporting/debugging only
        /// </summary>
        public List<string> ParentSegments => SplitSegments
            .Select(splitId => splitId.Split("_Split_")[0])
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Pendant cluster with single entry point
    /// </summary>
    public class PendantCluster
    {
        public string EntryIntersectionId { get; set; } = "";

        /// <summary>
        /// Split segment IDs that enter the cluster from outside (feeders)
        /// </summary>
        public List<string> EntrySplitSegments { get; set; } = new();

        /// <summary>
        /// Parent segment IDs of entry splits (for AADT lookup)
        /// </summary>
        public List<string> EntryParentSegments { get; set; } = new();

        /// <summary>
        /// Split segment IDs within the cluster
        /// </summary>
        public HashSet<string> ClusterSplitSegments { get; set; } = new();

        /// <summary>
        /// Parent segment IDs within the cluster (derived from splits)
        /// </summary>
        public HashSet<string> ClusterParentSegments { get; set; } = new();

        public int Depth { get; set; }
    }

    /// <summary>
    /// Block-cut tree for pendant cluster identification
    /// </summary>
    private class BlockCutTree
    {
        public Dictionary<string, TreeNode> CutNodes { get; set; } = new();
        public Dictionary<int, TreeNode> BlockNodes { get; set; } = new();

        /// <summary>
        /// Split segment IDs for each block node (key = blockNodeId like "Block_0")
        /// </summary>
        public Dictionary<string, List<string>> SplitSegments { get; set; } = new();
    }

    private class TreeNode
    {
        public enum NodeType { Block, CutVertex }
        public NodeType Type { get; set; }
        public string Id { get; set; } = "";
        public List<string> Children { get; set; } = new();
        public string? Parent { get; set; }
        public int Depth { get; set; }
    }

    #endregion
}
