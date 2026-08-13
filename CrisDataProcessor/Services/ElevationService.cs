using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class ElevationService
{
    private readonly ILogger<ElevationService> _logger;
    private readonly CrisModelConfiguration _config;
    private readonly GdalCommandLineService _gdalService;

    public ElevationService(ILogger<ElevationService> logger, CrisModelConfiguration config, GdalCommandLineService gdalService)
    {
        _logger = logger;
        _config = config;
        _gdalService = gdalService;
    }

    private bool _noElevationSourceWarningLogged;

    public decimal CalculateBasicSlope(double startLat, double startLon, double endLat, double endLon)
    {
        // Slope cannot be derived from horizontal coordinates alone; without a real
        // elevation source we report 0 (flat) so downstream drainage-risk flags never
        // fire from fabricated values.
        if (!_noElevationSourceWarningLogged)
        {
            _logger.LogWarning("No elevation data source available; basic slope defaults to 0% for all segments");
            _noElevationSourceWarningLogged = true;
        }

        return 0m;
    }

    public async Task EnhanceRoadSegmentsWithCrashBasedSlope(List<RiskSegment> segments, List<CrashRecord> crashes, Dictionary<string, List<CrashRecord>> crashesBySegment)
    {
        _logger.LogInformation("Enhancing {SegmentCount} road segments with crash-based slope analysis", segments.Count);

        if (!_config.ProcessingOptions.EnableElevationAnalysis)
        {
            _logger.LogInformation("Elevation analysis disabled in configuration, skipping slope calculation");
            return;
        }

        // Initialize GDAL service if not already done
        var isInitialized = await _gdalService.InitializeAsync();
        if (!isInitialized)
        {
            _logger.LogWarning("GDAL service failed to initialize, falling back to midpoint slope calculation");
            await EnhanceRoadSegmentsWithDEMSlope(segments);
            return;
        }

        var processedCount = 0;
        foreach (var segment in segments)
        {
            try
            {
                // Find all crashes associated with this segment from spatial join results
                var segmentCrashes = crashesBySegment.TryGetValue(segment.SegmentId, out var crashes_for_segment)
                    ? crashes_for_segment
                    : new List<CrashRecord>();

                if (segmentCrashes.Any())
                {
                    // Use crash-based slope analysis
                    var crashSlopes = segmentCrashes
                        .Where(c => c.SlopeAtLocation > 0) // Only crashes with valid slope data
                        .Select(c => c.SlopeAtLocation)
                        .ToList();

                    if (crashSlopes.Any())
                    {
                        // Calculate composite slope metrics from crash locations
                        var avgSlope = crashSlopes.Average();
                        var maxSlope = crashSlopes.Max();
                        var minSlope = crashSlopes.Min();

                        // Use average slope for the segment, but store max for high-risk assessment
                        var slopePercentage = _gdalService.ConvertSlopeToPercentage(avgSlope);
                        var maxSlopePercentage = _gdalService.ConvertSlopeToPercentage(maxSlope);

                        segment.SlopePercentage = slopePercentage;
                        segment.EnvironmentalFactors.SlopePercentage = slopePercentage;

                        // Use max slope for risk assessment if significantly higher
                        if (maxSlopePercentage > slopePercentage * 1.5m)
                        {
                            segment.EnvironmentalFactors.SlopePercentage = maxSlopePercentage;
                        }

                        _logger.LogDebug("Segment {SegmentId}: {CrashCount} crashes, avg slope {AvgSlope:F1}°, max {MaxSlope:F1}°",
                            segment.SegmentId, segmentCrashes.Count, avgSlope, maxSlope);
                    }
                    else
                    {
                        // Crashes exist but no valid slope data - fall back to midpoint
                        CalculateMidpointSlope(segment);
                    }
                }
                else
                {
                    // No crashes on this segment - use midpoint slope
                    CalculateMidpointSlope(segment);
                }

                processedCount++;

                if (processedCount % 100 == 0)
                {
                    _logger.LogDebug("Processed crash-based slope for {ProcessedCount}/{TotalCount} segments",
                        processedCount, segments.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate crash-based slope for segment {SegmentId}", segment.SegmentId);
                segment.SlopePercentage = 0;
            }
        }

        var highSlopeCount = segments.Count(s => s.SlopePercentage > _config.Thresholds.SlopeThreshold);
        var crashBasedCount = segments.Count(s => crashesBySegment.ContainsKey(s.SegmentId) &&
                                                    crashesBySegment[s.SegmentId].Any(c => c.SlopeAtLocation > 0));

        _logger.LogInformation("Enhanced {ProcessedCount}/{TotalCount} segments with crash-based slope data. " +
                             "Crash-based segments: {CrashBased}, High-slope segments (>{Threshold}%): {HighSlopeCount}",
            processedCount, segments.Count, crashBasedCount, _config.Thresholds.SlopeThreshold, highSlopeCount);

        // Log data quality metrics after processing
        _gdalService.LogDataQualityMetrics();
    }

    private void CalculateMidpointSlope(RiskSegment segment)
    {
        var midLat = (double)(segment.StartLatitude + segment.EndLatitude) / 2.0;
        var midLon = (double)(segment.StartLongitude + segment.EndLongitude) / 2.0;

        var slopeDegrees = _gdalService.GetSlopeAtCoordinate(midLat, midLon);
        var slopePercentage = _gdalService.ConvertSlopeToPercentage(slopeDegrees);

        segment.SlopePercentage = slopePercentage;
        segment.EnvironmentalFactors.SlopePercentage = slopePercentage;
    }

    public async Task EnhanceRoadSegmentsWithDEMSlope(List<RiskSegment> segments)
    {
        _logger.LogInformation("Enhancing {SegmentCount} road segments with DEM-based slope data", segments.Count);

        if (!_config.ProcessingOptions.EnableElevationAnalysis)
        {
            _logger.LogInformation("Elevation analysis disabled in configuration, skipping slope calculation");
            return;
        }

        // Initialize GDAL service if not already done
        var isInitialized = await _gdalService.InitializeAsync();
        if (!isInitialized)
        {
            _logger.LogWarning("GDAL service failed to initialize, falling back to basic slope calculation");
            EnhanceRoadSegmentsWithBasicSlope(segments);
            return;
        }

        var processedCount = 0;
        foreach (var segment in segments)
        {
            try
            {
                // Sample slope at the midpoint of the segment
                var midLat = (double)(segment.StartLatitude + segment.EndLatitude) / 2.0;
                var midLon = (double)(segment.StartLongitude + segment.EndLongitude) / 2.0;

                var slopeDegrees = _gdalService.GetSlopeAtCoordinate(midLat, midLon);
                var slopePercentage = _gdalService.ConvertSlopeToPercentage(slopeDegrees);
                var slopeCategory = _gdalService.CategorizeSlopeValue(slopeDegrees);

                segment.SlopePercentage = slopePercentage;
                segment.EnvironmentalFactors.SlopePercentage = slopePercentage;

                processedCount++;

                if (processedCount % 100 == 0)
                {
                    _logger.LogDebug("Processed DEM slope for {ProcessedCount}/{TotalCount} segments",
                        processedCount, segments.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate DEM slope for segment {SegmentId}", segment.SegmentId);
                segment.SlopePercentage = 0; // Default to flat if calculation fails
            }
        }

        var highSlopeCount = segments.Count(s => s.SlopePercentage > _config.Thresholds.SlopeThreshold);

        _logger.LogInformation("Enhanced {ProcessedCount}/{TotalCount} segments with DEM slope data. " +
                             "High-slope segments (>{Threshold}%): {HighSlopeCount}",
            processedCount, segments.Count, _config.Thresholds.SlopeThreshold, highSlopeCount);

        // Log data quality metrics after processing
        _gdalService.LogDataQualityMetrics();
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

    public async Task EnhanceCrashRecordsWithDEMSlope(List<CrashRecord> crashes)
    {
        _logger.LogInformation("Enhancing {CrashCount} crash records with DEM-based slope data", crashes.Count);

        if (!_config.ProcessingOptions.EnableElevationAnalysis)
        {
            _logger.LogInformation("Elevation analysis disabled in configuration, skipping crash slope calculation");
            return;
        }

        // Initialize GDAL service if not already done
        var isInitialized = await _gdalService.InitializeAsync();
        if (!isInitialized)
        {
            _logger.LogWarning("GDAL service failed to initialize, skipping crash slope enhancement");
            return;
        }

        var processedCount = 0;
        foreach (var crash in crashes)
        {
            try
            {
                var slopeDegrees = _gdalService.GetSlopeAtCoordinate((double)crash.Latitude, (double)crash.Longitude);
                var slopePercentage = _gdalService.ConvertSlopeToPercentage(slopeDegrees);
                var slopeCategory = _gdalService.CategorizeSlopeValue(slopeDegrees);

                crash.SlopeAtLocation = slopeDegrees;
                crash.SlopePercentage = slopePercentage;
                crash.SlopeCategory = slopeCategory;

                processedCount++;

                if (processedCount % 500 == 0)
                {
                    _logger.LogDebug("Processed DEM slope for {ProcessedCount}/{TotalCount} crashes",
                        processedCount, crashes.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate DEM slope for crash {CrashId}", crash.CrashId);
                crash.SlopeAtLocation = 0;
                crash.SlopePercentage = 0;
                crash.SlopeCategory = "Flat";
            }
        }

        var highSlopeCrashes = crashes.Count(c => c.SlopeAtLocation > _config.Thresholds.SlopeThreshold);

        _logger.LogInformation("Enhanced {ProcessedCount}/{TotalCount} crashes with DEM slope data. " +
                             "High-slope crashes (>{Threshold}°): {HighSlopeCrashes}",
            processedCount, crashes.Count, _config.Thresholds.SlopeThreshold, highSlopeCrashes);

        // Log data quality metrics after processing crashes
        _gdalService.LogDataQualityMetrics();
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