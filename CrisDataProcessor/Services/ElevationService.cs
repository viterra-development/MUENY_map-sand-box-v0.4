using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class ElevationService
{
    private readonly ILogger<ElevationService> _logger;
    private readonly CrisModelConfiguration _config;

    public ElevationService(ILogger<ElevationService> logger, CrisModelConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public decimal CalculateBasicSlope(double startLat, double startLon, double endLat, double endLon)
    {
        // Simplified slope calculation for Phase 1
        // Uses basic coordinate differences to estimate grade
        var latDiff = Math.Abs(endLat - startLat);
        var lonDiff = Math.Abs(endLon - startLon);

        // Simple grade estimation (0-15% typical range for roads)
        // Scale factor based on coordinate differences
        var basicSlope = (decimal)((latDiff + lonDiff) * 1000);

        // Cap at reasonable maximum slope for roads (15%)
        var clampedSlope = Math.Min(basicSlope, 15m);

        _logger.LogDebug("Basic slope calculated: lat_diff={LatDiff}, lon_diff={LonDiff} -> {Slope}%",
            latDiff, lonDiff, clampedSlope);

        return clampedSlope;
    }

    public void EnhanceRoadSegmentsWithBasicSlope(List<RiskSegment> segments)
    {
        _logger.LogInformation("Enhancing {SegmentCount} road segments with basic slope estimates", segments.Count);

        if (!_config.ProcessingOptions.EnableElevationAnalysis)
        {
            _logger.LogInformation("Elevation analysis disabled in configuration, skipping slope calculation");
            return;
        }

        var processedCount = 0;
        foreach (var segment in segments)
        {
            try
            {
                segment.SlopePercentage = CalculateBasicSlope(
                    (double)segment.StartLatitude, (double)segment.StartLongitude,
                    (double)segment.EndLatitude, (double)segment.EndLongitude);

                segment.EnvironmentalFactors.SlopePercentage = segment.SlopePercentage;
                processedCount++;

                if (processedCount % 100 == 0)
                {
                    _logger.LogDebug("Processed slope for {ProcessedCount}/{TotalCount} segments",
                        processedCount, segments.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate basic slope for segment {SegmentId}", segment.SegmentId);
                segment.SlopePercentage = 0; // Default to flat if calculation fails
            }
        }

        var highSlopeCount = segments.Count(s => s.SlopePercentage > _config.Thresholds.SlopeThreshold);

        _logger.LogInformation("Enhanced {ProcessedCount}/{TotalCount} segments with basic slope data. " +
                             "High-slope segments (>{Threshold}%): {HighSlopeCount}",
            processedCount, segments.Count, _config.Thresholds.SlopeThreshold, highSlopeCount);
    }

    public List<RiskSegment> IdentifyHighSlopeSegments(List<RiskSegment> segments)
    {
        var threshold = _config.Thresholds.SlopeThreshold;
        var highSlopeSegments = segments
            .Where(s => s.SlopePercentage > threshold)
            .OrderByDescending(s => s.SlopePercentage)
            .ToList();

        _logger.LogInformation("Identified {Count} high-slope segments (>{Threshold}%) out of {Total} total segments",
            highSlopeSegments.Count, threshold, segments.Count);

        return highSlopeSegments;
    }

    public ElevationStatistics CalculateElevationStatistics(List<RiskSegment> segments)
    {
        if (!segments.Any())
        {
            return new ElevationStatistics();
        }

        var slopes = segments.Select(s => s.SlopePercentage).ToList();

        var stats = new ElevationStatistics
        {
            TotalSegments = segments.Count,
            MinSlope = slopes.Min(),
            MaxSlope = slopes.Max(),
            AverageSlope = slopes.Average(),
            MedianSlope = CalculateMedian(slopes),
            SegmentsAboveThreshold = segments.Count(s => s.SlopePercentage > _config.Thresholds.SlopeThreshold),
            ThresholdPercentage = _config.Thresholds.SlopeThreshold
        };

        _logger.LogInformation("Basic slope statistics: Min={Min}%, Max={Max}%, Avg={Avg:F2}%, " +
                             "Above {Threshold}%: {AboveCount}/{Total} ({Percentage:F1}%)",
            stats.MinSlope, stats.MaxSlope, stats.AverageSlope,
            stats.ThresholdPercentage, stats.SegmentsAboveThreshold, stats.TotalSegments,
            (decimal)stats.SegmentsAboveThreshold / stats.TotalSegments * 100);

        return stats;
    }

    private decimal CalculateMedian(List<decimal> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var count = sorted.Count;

        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2;
        }
        else
        {
            return sorted[count / 2];
        }
    }
}

public class ElevationStatistics
{
    public int TotalSegments { get; set; }
    public decimal MinSlope { get; set; }
    public decimal MaxSlope { get; set; }
    public decimal AverageSlope { get; set; }
    public decimal MedianSlope { get; set; }
    public int SegmentsAboveThreshold { get; set; }
    public decimal ThresholdPercentage { get; set; }
}

// Phase 2 Enhancement Notes:
// - Raw DEM data available in /DEM directory
// - Processed elevation tiles in /upload-staging directory
// - Future integration should:
//   1. Read elevation values from DEM tiles at segment coordinates
//   2. Calculate actual slope using elevation difference over distance
//   3. Consider implementing tile caching for performance
//   4. Add support for hydrology data integration