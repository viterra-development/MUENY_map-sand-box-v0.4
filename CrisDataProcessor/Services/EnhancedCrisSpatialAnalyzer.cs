using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class EnhancedCrisSpatialAnalyzer
{
    private readonly ILogger<EnhancedCrisSpatialAnalyzer> _logger;
    private readonly RoadGeometryService _roadGeometryService;
    private readonly double _proximityThresholdMeters;

    public EnhancedCrisSpatialAnalyzer(
        ILogger<EnhancedCrisSpatialAnalyzer> logger,
        RoadGeometryService roadGeometryService,
        double proximityThresholdMeters = 50.0)
    {
        _logger = logger;
        _roadGeometryService = roadGeometryService;
        _proximityThresholdMeters = proximityThresholdMeters;
    }

    public async Task<List<EnhancedSpatialJoinResult>> SpatialJoinCrashesToRoadsAsync(
        List<CrashRecord> crashes,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Performing enhanced spatial join of {CrashCount} crashes to road network at {RoadPath}",
            crashes.Count, roadGeoJsonPath);

        // Load road data into our enhanced geometry service
        await _roadGeometryService.LoadRoadDataAsync(roadGeoJsonPath);
        var qualityMetrics = _roadGeometryService.GetQualityMetrics();

        _logger.LogInformation("Road geometry loaded: {TotalRoads} roads, {PercentNamed:F1}% named",
            qualityMetrics.TotalRoadFeatures, qualityMetrics.PercentageWithNames);

        var results = new List<EnhancedSpatialJoinResult>();
        var matchedCount = 0;

        foreach (var crash in crashes)
        {
            var roadMatch = _roadGeometryService.FindBestRoadMatch(
                (double)crash.Latitude,
                (double)crash.Longitude,
                _proximityThresholdMeters);

            if (roadMatch != null)
            {
                matchedCount++;

                results.Add(new EnhancedSpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = roadMatch.Road.LinearId,
                    DistanceToRoad = (decimal)roadMatch.DistanceMeters,
                    WithinThreshold = roadMatch.IsWithinTolerance,
                    RoadFeature = roadMatch.Road // Include the full road feature
                });
            }
            else
            {
                // Create unmatched result for tracking
                results.Add(new EnhancedSpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = "",
                    DistanceToRoad = decimal.MaxValue,
                    WithinThreshold = false,
                    RoadFeature = null
                });
            }
        }

        _logger.LogInformation("Enhanced spatial join completed: {MatchedCount}/{TotalCrashes} crashes matched to road segments within {Threshold}m",
            matchedCount, crashes.Count, _proximityThresholdMeters);

        return results;
    }

    public Dictionary<string, List<CrashRecord>> GroupCrashesByRoadSegment(List<EnhancedSpatialJoinResult> spatialJoinResults)
    {
        var crashesBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Crash).ToList());

        _logger.LogInformation("Grouped crashes into {SegmentCount} road segments", crashesBySegment.Count);
        return crashesBySegment;
    }
}

// Enhanced spatial join result that includes road geometry information
public class EnhancedSpatialJoinResult
{
    public CrashRecord Crash { get; set; } = null!;
    public string RoadSegmentId { get; set; } = "";
    public decimal DistanceToRoad { get; set; }
    public bool WithinThreshold { get; set; }
    public RoadFeature? RoadFeature { get; set; } // Enhanced: includes full road geometry
}