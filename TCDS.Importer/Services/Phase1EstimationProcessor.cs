using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

/// <summary>
/// Phase 1 + 1.5 processor for traffic AADT estimation using Spatial Interpolation (IDW)
/// with Network Topology Validation
/// </summary>
public class Phase1EstimationProcessor
{
    private readonly ILogger<Phase1EstimationProcessor> _logger;
    private readonly TrafficEstimationService _estimationService;
    private readonly TrafficEstimationValidationService _validationService;
    private readonly NetworkTopologyValidator _topologyValidator;

    public Phase1EstimationProcessor(
        ILogger<Phase1EstimationProcessor> logger,
        TrafficEstimationService estimationService,
        TrafficEstimationValidationService validationService,
        NetworkTopologyValidator topologyValidator)
    {
        _logger = logger;
        _estimationService = estimationService;
        _validationService = validationService;
        _topologyValidator = topologyValidator;
    }

    /// <summary>
    /// Runs the complete Phase 1 estimation pipeline
    /// </summary>
    public async Task<string> RunPhase1EstimationAsync(
        string inputGeoJsonPath,
        string outputDirectory,
        bool performCrossValidation = true)
    {
        _logger.LogInformation("═══════════════════════════════════════════════════════════");
        _logger.LogInformation("  PHASE 1: Traffic AADT Estimation");
        _logger.LogInformation("  Method: Spatial Interpolation (Inverse Distance Weighting)");
        _logger.LogInformation("═══════════════════════════════════════════════════════════");

        try
        {
            // Step 1: Load road segments
            _logger.LogInformation("\n[Step 1/6] Loading road data...");
            var segments = await _estimationService.LoadRoadSegmentsAsync(inputGeoJsonPath);

            // Step 2: Cross-validation (optional)
            if (performCrossValidation)
            {
                _logger.LogInformation("\n[Step 2/6] Performing cross-validation...");
                var roadsWithData = segments.Where(s => s.ExistingAadt.HasValue).ToList();

                if (roadsWithData.Count >= 50) // Need sufficient data for meaningful validation
                {
                    var validationMetrics = _validationService.PerformCrossValidation(roadsWithData, folds: 5);

                    // Check if validation meets targets
                    if (validationMetrics.R2 < 0.70)
                    {
                        _logger.LogWarning("⚠️  Warning: R² score {R2:F3} is below Phase 1 target of 0.70",
                            validationMetrics.R2);
                        _logger.LogWarning("    Consider adjusting interpolation parameters or investigating data quality");
                    }
                    else
                    {
                        _logger.LogInformation("✅ Validation meets Phase 1 targets (R² = {R2:F3} >= 0.70)",
                            validationMetrics.R2);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️  Insufficient data for cross-validation ({Count} roads with AADT). Skipping...",
                        roadsWithData.Count);
                }
            }
            else
            {
                _logger.LogInformation("\n[Step 2/6] Skipping cross-validation (disabled)");
            }

            // Step 3: Estimate AADT for roads without data
            _logger.LogInformation("\n[Step 3/7] Estimating AADT for roads without traffic data...");
            var summary = _estimationService.EstimateTrafficForAllRoads(segments);

            // Log initial estimates for debugging
            _logger.LogInformation("Initial AADT estimation complete:");
            foreach (var segment in segments.Take(10))  // Log first 10 as sample
            {
                var source = segment.ExistingAadt.HasValue ? "ACTUAL_COUNT" :
                            (segment.Estimation != null ? "INTERPOLATED" : "NONE");
                var aadt = segment.Estimation?.EstimatedAadt ?? segment.ExistingAadt ?? 0;
                _logger.LogDebug("  {RoadName} ({LinearId}): {AADT} AADT [{Source}]",
                    segment.FullName ?? "Unknown", segment.LinearId, aadt, source);
            }

            // Step 4: Apply network topology constraints (Phase 1.6 - Pendant Cluster Propagation)
            _logger.LogInformation("\n[Step 4/7] Applying network topology constraints (Phase 1.6)...");

            // Capture initial AADT values before applying constraints (for audit trail)
            var initialAadtValues = segments.ToDictionary(
                s => s.LinearId,
                s => s.ExistingAadt ?? s.Estimation?.EstimatedAadt ?? 0
            );

            _logger.LogInformation("Building road network graph...");
            _topologyValidator.BuildNetworkGraph(segments);

            _logger.LogInformation("Applying pendant cluster constraints with depth-ordered propagation...");
            var (violations, auditEntries) = ApplyPendantClusterPropagation(segments);

            _logger.LogInformation("  ✓ Applied topology constraints to {Count} road segments", segments.Count);
            _logger.LogInformation("  ✓ Corrected {Corrected} topology violations", violations.Count);
            _logger.LogInformation("  ✓ AADT changes: {Changes} roads had constraints applied", auditEntries.Count);

            // Save audit trail
            await SaveAuditTrailAsync(segments, auditEntries, initialAadtValues, outputDirectory);

            var topologyReport = _topologyValidator.GenerateReport(violations);

            // Step 5: Check functional class compliance
            _logger.LogInformation("\n[Step 5/7] Checking functional class compliance...");
            var compliance = _validationService.CheckFunctionalClassCompliance(segments);

            // Step 6: Save results
            _logger.LogInformation("\n[Step 6/7] Saving results...");
            var outputFileName = "parker-roads-with-traffic-phase1.geojson";
            var outputPath = Path.Combine(outputDirectory, outputFileName);

            // Ensure output directory exists
            Directory.CreateDirectory(outputDirectory);

            var savedPath = await _estimationService.SaveEstimatedRoadsAsync(segments, summary, outputPath);

            // Step 7: Generate summary report
            _logger.LogInformation("\n[Step 7/7] Generating summary report...");
            await GenerateSummaryReportAsync(summary, compliance, topologyReport, outputDirectory);

            _logger.LogInformation("\n═══════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ PHASE 1 ESTIMATION COMPLETE");
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("Output file: {Path}", savedPath);
            _logger.LogInformation("Summary report: {Path}", Path.Combine(outputDirectory, "phase1-summary.json"));

            return savedPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Phase 1 estimation failed");
            throw;
        }
    }

    /// <summary>
    /// Generates a JSON summary report of the estimation results
    /// </summary>
    private async Task GenerateSummaryReportAsync(
        EstimationSummary summary,
        Dictionary<RoadHierarchy, (int Min, int Max, int Violations)> compliance,
        TopologyValidationReport topologyReport,
        string outputDirectory)
    {
        var report = new
        {
            phase = "Phase 1 + 1.5: Spatial Interpolation (IDW) with Network Topology Validation",
            generatedAt = DateTime.UtcNow,
            summary = new
            {
                totalRoads = summary.TotalRoads,
                roadsWithExistingData = summary.RoadsWithExistingData,
                roadsEstimated = summary.RoadsEstimated,
                roadsUnableToEstimate = summary.RoadsUnableToEstimate,
                averageConfidence = Math.Round(summary.AverageConfidence, 3),
                // Guard against 0 roads without data (Infinity is not serializable)
                estimationRate = summary.TotalRoads - summary.RoadsWithExistingData > 0
                    ? Math.Round(summary.RoadsEstimated * 100.0 / (summary.TotalRoads - summary.RoadsWithExistingData), 1)
                    : 0
            },
            estimatedByHierarchy = summary.EstimatedByHierarchy
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new
                    {
                        count = kvp.Value,
                        percentage = Math.Round(kvp.Value * 100.0 / summary.RoadsEstimated, 1)
                    }),
            estimationMethods = summary.EstimationMethodCounts
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value),
            networkTopology = new
            {
                totalRoadsAnalyzed = topologyReport.TotalRoadsAnalyzed,
                deadEndRoads = topologyReport.DeadEndRoads,
                isolatedRoads = topologyReport.IsolatedRoads,
                topologyCorrectionsApplied = topologyReport.TopologyCorrectionsApplied,
                deadEndViolationsFixed = topologyReport.DeadEndViolationsFixed,
                lowConnectivityCorrections = topologyReport.LowConnectivityCorrections,
                averageConnectivity = Math.Round(topologyReport.AverageConnectivity, 2),
                medianConnectivity = topologyReport.MedianConnectivity,
                topViolations = topologyReport.TopViolations.Take(10).Select(v => new
                {
                    roadName = v.RoadName,
                    violationType = v.ViolationType,
                    originalEstimate = v.OriginalEstimate,
                    correctedEstimate = v.CorrectedEstimate,
                    difference = v.Difference,
                    percentChange = Math.Round(v.PercentChange, 1),
                    reason = v.Reason
                }).ToList()
            },
            functionalClassCompliance = compliance
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new
                    {
                        expectedRange = new { min = kvp.Value.Min, max = kvp.Value.Max },
                        violations = kvp.Value.Violations
                    }),
            recommendations = GenerateRecommendations(summary, compliance, topologyReport)
        };

        var reportPath = Path.Combine(outputDirectory, "phase1-summary.json");
        var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(reportPath, json);

        _logger.LogInformation("Summary report saved to: {Path}", reportPath);
    }

    /// <summary>
    /// Generates recommendations based on estimation results
    /// </summary>
    private List<string> GenerateRecommendations(
        EstimationSummary summary,
        Dictionary<RoadHierarchy, (int Min, int Max, int Violations)> compliance,
        TopologyValidationReport topologyReport)
    {
        var recommendations = new List<string>();

        // Check estimation rate
        var roadsWithoutData = summary.TotalRoads - summary.RoadsWithExistingData;
        var estimationRate = roadsWithoutData > 0 ? summary.RoadsEstimated * 100.0 / roadsWithoutData : 0;
        if (estimationRate < 80)
        {
            recommendations.Add($"Low estimation rate ({estimationRate:F1}%). Consider increasing MaxSearchRadius or decreasing MinConfidence threshold.");
        }

        // Check confidence
        if (summary.AverageConfidence < 0.5)
        {
            recommendations.Add($"Low average confidence ({summary.AverageConfidence:F2}). Consider adjusting DecayLambda parameter or collecting more reference data.");
        }

        // Check compliance violations
        var totalViolations = compliance.Sum(kvp => kvp.Value.Violations);
        if (totalViolations > 0)
        {
            recommendations.Add($"{totalViolations} functional class compliance violations detected. Review estimation parameters and reference data quality.");
        }

        // Check topology corrections
        if (topologyReport.TopologyCorrectionsApplied > 0)
        {
            recommendations.Add($"Phase 1.5 network topology validation corrected {topologyReport.TopologyCorrectionsApplied} violations " +
                              $"({topologyReport.DeadEndViolationsFixed} dead-end, {topologyReport.LowConnectivityCorrections} low-connectivity).");
        }

        // Report on dead-ends
        if (topologyReport.DeadEndRoads > 0)
        {
            recommendations.Add($"Identified {topologyReport.DeadEndRoads} dead-end roads in the network. These were constrained to 80% of connector road traffic.");
        }

        // Check if ready for Phase 2
        if (estimationRate >= 80 && summary.AverageConfidence >= 0.5 && totalViolations < summary.RoadsEstimated * 0.1)
        {
            recommendations.Add("Phase 1 + 1.5 results look good! Ready to proceed to Phase 2 (Regression-Enhanced Interpolation) for improved accuracy.");
        }

        return recommendations;
    }

    /// <summary>
    /// Saves a comprehensive audit trail CSV showing AADT estimation pipeline for all roads
    /// </summary>
    private async Task SaveAuditTrailAsync(
        List<RoadSegment> segments,
        List<(string LinearId, string RoadName, int InitialAadt, int FinalAadt, string ConstraintType, string Reason)> constraintChanges,
        Dictionary<string, int> initialAadtValues,
        string outputDirectory)
    {
        var auditPath = Path.Combine(outputDirectory, "phase1-aadt-audit-trail.csv");

        using (var writer = new StreamWriter(auditPath))
        {
            // Write header
            await writer.WriteLineAsync("LinearId,RoadName,DataSource,InitialAADT,ConstraintApplied,ConstraintType,FinalAADT,Change,PercentChange,Reason");

            // Create lookup for constraint changes (use last constraint if multiple constraints applied)
            var constraintLookup = constraintChanges
                .GroupBy(c => c.LinearId)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var segment in segments)
            {
                var linearId = segment.LinearId;
                var roadName = (segment.FullName ?? "Unknown").Replace(",", ";"); // CSV-safe

                // Determine data source
                var dataSource = segment.ExistingAadt.HasValue ? "ACTUAL_COUNT" :
                                (segment.Estimation != null ? "INTERPOLATED" : "NO_DATA");

                // Use captured initial AADT (before constraints were applied)
                var initialAadt = initialAadtValues.GetValueOrDefault(linearId, 0);
                var finalAadt = segment.Estimation?.EstimatedAadt ?? segment.ExistingAadt ?? 0;

                // Check if constraint was applied
                var constraintApplied = constraintLookup.ContainsKey(linearId);
                var constraintType = constraintApplied ? constraintLookup[linearId].ConstraintType : "NONE";
                var reason = constraintApplied ? constraintLookup[linearId].Reason.Replace(",", ";") : "No constraint needed";

                if (constraintApplied)
                {
                    initialAadt = constraintLookup[linearId].InitialAadt;
                    finalAadt = constraintLookup[linearId].FinalAadt;
                }

                var change = finalAadt - initialAadt;
                var percentChange = initialAadt > 0 ? Math.Round((change * 100.0) / initialAadt, 1) : 0;

                await writer.WriteLineAsync(
                    $"{linearId},{roadName},{dataSource},{initialAadt},{constraintApplied},{constraintType},{finalAadt},{change},{percentChange},\"{reason}\"");
            }
        }

        _logger.LogInformation("  ✓ Saved audit trail: {Path}", auditPath);
        _logger.LogInformation("    Use this file to track AADT estimation pipeline for any road");
    }

    /// <summary>
    /// Apply pendant cluster constraints using depth-ordered propagation to ensure feeders are processed first
    /// </summary>
    private (List<TopologyViolation> violations, List<(string LinearId, string RoadName, int InitialAadt, int FinalAadt, string ConstraintType, string Reason)> auditEntries)
        ApplyPendantClusterPropagation(List<RoadSegment> segments)
    {
        var violations = new List<TopologyViolation>();
        var auditEntries = new List<(string LinearId, string RoadName, int InitialAadt, int FinalAadt, string ConstraintType, string Reason)>();

        // Step A: Build baseline AADT lookup table (parentAadt)
        _logger.LogDebug("Building baseline AADT lookup table for {Count} segments", segments.Count);
        var parentAadt = new Dictionary<string, int>();
        var segmentLookup = new Dictionary<string, RoadSegment>();

        foreach (var segment in segments)
        {
            // Prefer measured AADT, fall back to modeled estimate
            var aadt = segment.ExistingAadt ?? segment.Estimation?.EstimatedAadt ?? 0;
            parentAadt[segment.LinearId] = aadt;
            segmentLookup[segment.LinearId] = segment;
        }

        // Step B: Get pendant clusters sorted by depth (ascending)
        var pendantClusters = _topologyValidator.GetPendantClusters();
        var sortedClusters = pendantClusters.OrderBy(c => c.Depth).ToList();

        _logger.LogInformation("Processing {ClusterCount} pendant clusters in depth order (max depth: {MaxDepth})",
            sortedClusters.Count,
            sortedClusters.Any() ? sortedClusters.Max(c => c.Depth) : 0);

        var clustersCapped = 0;
        var clustersSkipped = 0;
        var missingFeeders = 0;

        // Step C: One-pass pendant cap propagation
        foreach (var cluster in sortedClusters)
        {
            // Get total AADT from entry parent segments (feeders from main network)
            double totalEntryAadt = 0;
            var entryRoadNames = new List<string>();
            var hasMissingFeeder = false;

            foreach (var entryParentId in cluster.EntryParentSegments)
            {
                if (!parentAadt.TryGetValue(entryParentId, out var feederAadt))
                {
                    _logger.LogWarning("Pendant cluster at {Entry} has missing feeder {FeederId}. Cluster members: {ClusterSize}",
                        cluster.EntryIntersectionId, entryParentId, cluster.ClusterParentSegments.Count);
                    hasMissingFeeder = true;
                    missingFeeders++;
                    continue;
                }

                if (feederAadt == 0)
                {
                    _logger.LogWarning("Pendant cluster at {Entry} has zero AADT feeder {FeederId}. Cluster members: {ClusterSize}",
                        cluster.EntryIntersectionId, entryParentId, cluster.ClusterParentSegments.Count);
                    hasMissingFeeder = true;
                    missingFeeders++;
                    continue;
                }

                totalEntryAadt += feederAadt;

                if (segmentLookup.TryGetValue(entryParentId, out var entrySegment))
                {
                    entryRoadNames.Add(entrySegment.FullName ?? entryParentId);
                }
            }

            if (totalEntryAadt == 0)
            {
                _logger.LogDebug("Skipping cluster at {Entry} - no valid feeder AADT", cluster.EntryIntersectionId);
                clustersSkipped++;
                continue;
            }

            // Calculate cluster capacity with depth-based attenuation
            double attenuationFactor = Math.Pow(0.8, cluster.Depth); // 0.8^depth
            var clusterCapacity = (int)(totalEntryAadt * attenuationFactor);

            // Apply cap to all cluster member roads
            foreach (var clusterMemberId in cluster.ClusterParentSegments)
            {
                if (!parentAadt.TryGetValue(clusterMemberId, out var currentAadt))
                    continue;

                if (currentAadt <= clusterCapacity)
                    continue; // No cap needed

                // Apply constraint
                var originalAadt = currentAadt;
                parentAadt[clusterMemberId] = clusterCapacity; // Write back to lookup table

                // Build warning message
                var entryRoadDescription = entryRoadNames.Count == 1
                    ? $"entry road '{entryRoadNames[0]}'"
                    : $"{entryRoadNames.Count} entry roads ({string.Join(", ", entryRoadNames.Take(2))}{(entryRoadNames.Count > 2 ? ", ..." : "")})";

                var warningMsg = cluster.Depth > 1
                    ? $"Nested pendant cluster (depth {cluster.Depth}): Capped to {clusterCapacity:N0} AADT " +
                      $"({attenuationFactor:P0} of {entryRoadDescription}: {totalEntryAadt:N0})"
                    : $"Pendant cluster constraint: Capped to {clusterCapacity:N0} AADT " +
                      $"({attenuationFactor:P0} of {entryRoadDescription}: {totalEntryAadt:N0})";

                if (segmentLookup.TryGetValue(clusterMemberId, out var memberSegment))
                {
                    // Track for audit
                    auditEntries.Add((clusterMemberId, memberSegment.FullName ?? "Unknown",
                                     originalAadt, clusterCapacity,
                                     "PendantCluster", warningMsg));

                    // Track as violation
                    violations.Add(new TopologyViolation
                    {
                        LinearId = clusterMemberId,
                        RoadName = memberSegment.FullName,
                        ViolationType = "PendantCluster",
                        OriginalEstimate = originalAadt,
                        CorrectedEstimate = clusterCapacity,
                        Difference = originalAadt - clusterCapacity,
                        PercentChange = originalAadt > 0 ? (originalAadt - clusterCapacity) * 100.0 / originalAadt : 0,
                        Reason = warningMsg
                    });

                    _logger.LogDebug("Pendant cluster cap: {Road} ({LinearId}): {Before:N0} → {After:N0} (depth={Depth})",
                        memberSegment.FullName, clusterMemberId, originalAadt, clusterCapacity, cluster.Depth);
                }

                clustersCapped++;
            }
        }

        // Step D: Synchronize capped values back to RoadSegment objects
        _logger.LogDebug("Synchronizing capped values back to {Count} road segments", segments.Count);
        foreach (var segment in segments)
        {
            if (parentAadt.TryGetValue(segment.LinearId, out var finalAadt))
            {
                if (segment.Estimation != null)
                {
                    segment.Estimation.EstimatedAadt = finalAadt;
                }
            }
        }

        // Log diagnostics
        _logger.LogInformation("  ✓ Pendant cluster propagation complete:");
        _logger.LogInformation("    - Clusters processed: {Total}", sortedClusters.Count);
        _logger.LogInformation("    - Roads capped: {Capped}", clustersCapped);
        _logger.LogInformation("    - Clusters skipped (missing feeders): {Skipped}", clustersSkipped);
        if (missingFeeders > 0)
        {
            _logger.LogWarning("    ⚠️  Missing or zero feeders detected: {Count}", missingFeeders);
        }

        return (violations, auditEntries);
    }
}
