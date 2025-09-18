using System.Text.Json;
using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

public class EnhancedRoadTrafficMerger
{
    private readonly ILogger<EnhancedRoadTrafficMerger> _logger;
    private readonly TypeBasedTrafficMatcher _trafficMatcher;
    private readonly AadtValidationService _validationService;

    public EnhancedRoadTrafficMerger(
        ILogger<EnhancedRoadTrafficMerger> logger,
        TypeBasedTrafficMatcher trafficMatcher,
        AadtValidationService validationService)
    {
        _logger = logger;
        _trafficMatcher = trafficMatcher;
        _validationService = validationService;
    }

    public async Task<string> MergeRoadTrafficDataFromMasterAsync(
        string roadGeoJsonPath,
        string masterDataPath,
        string outputDirectory,
        bool excludeInactiveStations = true)
    {
        _logger.LogInformation("🚦 Starting Enhanced Traffic-Road Data Merger (using MASTER data)");

        // 1. Load road data
        var roadsData = await LoadRoadDataAsync(roadGeoJsonPath);

        // 2. Load and enhance traffic data
        var enhancedTrafficLocations = await LoadAndEnhanceTrafficDataAsync(masterDataPath, excludeInactiveStations);

        // 3. Perform type-based matching
        var matchResults = PerformTypeBasedMatching(roadsData.Features, enhancedTrafficLocations);

        // 4. Generate quality report
        var qualityReport = _validationService.GenerateQualityReport(matchResults);
        LogQualityReport(qualityReport);

        // 5. Create enhanced road features with strong typing
        var enhancedFeatureCollection = CreateEnhancedFeatureCollection(roadsData.Features, matchResults, qualityReport);

        // 6. Save results
        var outputPath = await SaveEnhancedDatasetAsync(enhancedFeatureCollection, outputDirectory);

        // 7. Display comprehensive statistics
        DisplayComprehensiveStatistics(enhancedFeatureCollection);

        _logger.LogInformation("✅ Enhanced Traffic-Road merger complete!");
        return outputPath;
    }

    private async Task<GeoJsonFeatureCollection> LoadRoadDataAsync(string roadGeoJsonPath)
    {
        _logger.LogInformation("📍 Loading Parker County roads from: {RoadPath}", roadGeoJsonPath);

        if (!File.Exists(roadGeoJsonPath))
            throw new FileNotFoundException($"Roads file not found: {roadGeoJsonPath}");

        var roadsJson = await File.ReadAllTextAsync(roadGeoJsonPath);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var roadsData = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(roadsJson, jsonOptions);
        if (roadsData?.Features == null)
            throw new InvalidOperationException("Failed to parse roads GeoJSON data");

        _logger.LogInformation("   ✅ Loaded {RoadCount} road features", roadsData.Features.Count);
        return roadsData;
    }

    private async Task<List<EnhancedTrafficLocation>> LoadAndEnhanceTrafficDataAsync(string masterDataPath, bool excludeInactiveStations)
    {
        _logger.LogInformation("🚥 Loading MASTER traffic data from: {MasterPath}", masterDataPath);

        if (!File.Exists(masterDataPath))
            throw new FileNotFoundException($"MASTER data file not found: {masterDataPath}");

        var masterJson = await File.ReadAllTextAsync(masterDataPath);
        var masterDocument = JsonSerializer.Deserialize<JsonElement>(masterJson);

        if (!masterDocument.TryGetProperty("records", out var recordsElement))
            throw new InvalidOperationException("MASTER data file does not contain 'records' property");

        var trafficRecords = JsonSerializer.Deserialize<List<TrafficCountData>>(recordsElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (trafficRecords == null)
            throw new InvalidOperationException("Failed to parse MASTER traffic data");

        // Filter records
        var validTrafficRecords = trafficRecords
            .Where(r => r.LocationInfo?.Latitude.HasValue == true &&
                       r.LocationInfo?.Longitude.HasValue == true &&
                       r.AadtData?.Any() == true)
            .Where(r => !excludeInactiveStations || r.LocationInfo?.Active != "No")
            .ToList();

        _logger.LogInformation("   ✅ Loaded {TotalRecords} total records, {ValidCount} with valid coordinates and AADT data",
            trafficRecords.Count, validTrafficRecords.Count);

        // Convert to enhanced locations
        var enhancedLocations = _trafficMatcher.ConvertToEnhancedLocations(validTrafficRecords);

        // Validate AADT values
        foreach (var location in enhancedLocations)
        {
            if (location.Aadt.HasValue)
            {
                location.ValidationResult = _validationService.ValidateAadtForLocation(location, enhancedLocations);
            }
        }

        LogTrafficLocationStatistics(enhancedLocations);
        return enhancedLocations;
    }

    private void LogTrafficLocationStatistics(List<EnhancedTrafficLocation> locations)
    {
        var stats = locations.GroupBy(l => l.TargetRoadHierarchy)
            .ToDictionary(g => g.Key, g => g.Count());

        _logger.LogInformation("   📊 Enhanced traffic locations by type:");
        foreach (var (hierarchy, count) in stats.OrderBy(x => x.Key))
        {
            _logger.LogInformation("     • {Hierarchy}: {Count}", hierarchy, count);
        }
    }

    private List<TrafficMatchResult> PerformTypeBasedMatching(List<GeoJsonFeature> roads, List<EnhancedTrafficLocation> trafficLocations)
    {
        _logger.LogInformation("🔍 Performing type-based traffic matching for {RoadCount} roads", roads.Count);

        var matchResults = new List<TrafficMatchResult>();

        foreach (var road in roads)
        {
            var matchResult = _trafficMatcher.MatchTrafficToRoad(road, trafficLocations);
            matchResults.Add(matchResult);
        }

        var matchCount = matchResults.Count(r => r.IsMatch);
        _logger.LogInformation("   ✅ Found {MatchCount} traffic matches out of {TotalRoads} roads ({MatchRate:F1}%)",
            matchCount, roads.Count, (matchCount / (double)roads.Count * 100));

        // Log match type statistics
        var matchTypeStats = matchResults
            .Where(r => r.IsMatch)
            .GroupBy(r => r.MatchType)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (matchType, count) in matchTypeStats)
        {
            _logger.LogInformation("     • {MatchType}: {Count} matches", matchType, count);
        }

        return matchResults;
    }

    private void LogQualityReport(QualityReport report)
    {
        _logger.LogInformation("📊 Data Quality Report:");

        if (report.InterstateAnomalies.Any())
        {
            _logger.LogWarning("   ⚠️  Interstate Anomalies: {Count}", report.InterstateAnomalies.Count);
            foreach (var anomaly in report.InterstateAnomalies.Take(3))
            {
                _logger.LogWarning("     • {RoadName}: {Description}", anomaly.RoadName, anomaly.Description);
            }
        }

        if (report.RampToMainlineContamination.Any())
        {
            _logger.LogWarning("   🚨 Ramp Contamination: {Count}", report.RampToMainlineContamination.Count);
            foreach (var contamination in report.RampToMainlineContamination.Take(3))
            {
                _logger.LogWarning("     • {RoadName}: {Description}", contamination.RoadName, contamination.Description);
            }
        }

        if (report.AadtOutliers.Any())
        {
            _logger.LogInformation("   📈 AADT Outliers: {Count}", report.AadtOutliers.Count);
        }

        var totalIssues = report.InterstateAnomalies.Count + report.RampToMainlineContamination.Count + report.AadtOutliers.Count;
        _logger.LogInformation("   ✅ Total Quality Issues: {Issues}", totalIssues);
    }

    private EnhancedGeoJsonFeatureCollection CreateEnhancedFeatureCollection(
        List<GeoJsonFeature> roads,
        List<TrafficMatchResult> matchResults,
        QualityReport qualityReport)
    {
        var enhancedFeatures = new List<EnhancedGeoJsonFeature>();
        var totalWarnings = 0;

        for (int i = 0; i < roads.Count; i++)
        {
            var road = roads[i];
            var matchResult = matchResults[i];
            var classification = _trafficMatcher.ClassifyRoad(road);

            var enhancedProperties = new EnhancedRoadProperties
            {
                // Copy original properties
                LinearId = GetPropertyValue(road.Properties, "LINEARID"),
                FullName = GetPropertyValue(road.Properties, "FULLNAME"),
                RoadType = GetPropertyValue(road.Properties, "RTTYP"),
                Mtfcc = GetPropertyValue(road.Properties, "MTFCC"),

                // Add road classification
                Classification = classification
            };

            if (matchResult.IsMatch && matchResult.MatchedLocation != null)
            {
                // Add strongly typed traffic data
                enhancedProperties.Traffic = matchResult.ToTrafficData();
                enhancedProperties.TrafficMatch = matchResult.ToMatchMetadata();

                // Add validation warnings
                if (matchResult.MatchedLocation.ValidationResult.HasWarnings)
                {
                    enhancedProperties.ValidationWarnings = matchResult.MatchedLocation.ValidationResult.Warnings;
                    totalWarnings += matchResult.MatchedLocation.ValidationResult.Warnings.Count;
                }
            }

            var enhancedFeature = new EnhancedGeoJsonFeature
            {
                Type = "Feature",
                Geometry = road.Geometry,
                Properties = enhancedProperties
            };

            enhancedFeatures.Add(enhancedFeature);
        }

        // Calculate statistics
        var matchingStats = CalculateMatchingStatistics(enhancedFeatures, matchResults);

        var qualitySummary = new QualitySummary
        {
            InterstateAnomalies = qualityReport.InterstateAnomalies.Count,
            RampContamination = qualityReport.RampToMainlineContamination.Count,
            AadtOutliers = qualityReport.AadtOutliers.Count,
            TotalWarnings = totalWarnings,
            DataQualityScore = CalculateDataQualityScore(qualityReport, matchingStats)
        };

        return new EnhancedGeoJsonFeatureCollection
        {
            Type = "FeatureCollection",
            Metadata = new EnhancedDatasetMetadata
            {
                Title = "Parker County Roads with Enhanced Traffic Data",
                Description = "Road segments enhanced with type-based AADT traffic count matching and validation",
                TotalFeatures = enhancedFeatures.Count,
                GeneratedAt = DateTime.UtcNow,
                Source = "Texas Department of Transportation TCDS MASTER data + TIGER/Line",
                QualitySummary = qualitySummary,
                MatchingStatistics = matchingStats
            },
            Features = enhancedFeatures
        };
    }

    private MatchingStatistics CalculateMatchingStatistics(List<EnhancedGeoJsonFeature> features, List<TrafficMatchResult> matchResults)
    {
        var matchedFeatures = features.Where(f => f.Properties.Traffic != null).ToList();

        var matchTypeCount = matchResults
            .Where(r => r.IsMatch)
            .GroupBy(r => r.MatchType)
            .ToDictionary(g => g.Key, g => g.Count());

        var aadtByRoadType = matchedFeatures
            .Where(f => f.Properties.Traffic?.Aadt != null && f.Properties.Classification != null)
            .GroupBy(f => f.Properties.Classification!.Hierarchy.ToString())
            .ToDictionary(g => g.Key, g => CalculateAadtStatistics(g.Select(f => f.Properties.Traffic!.Aadt!.Value)));

        return new MatchingStatistics
        {
            TotalRoads = features.Count,
            MatchedRoads = matchedFeatures.Count,
            MatchTypeCount = matchTypeCount,
            AadtByRoadType = aadtByRoadType
        };
    }

    private AadtStatistics CalculateAadtStatistics(IEnumerable<int> aadtValues)
    {
        var values = aadtValues.OrderBy(x => x).ToList();
        if (!values.Any()) return new AadtStatistics();

        return new AadtStatistics
        {
            Count = values.Count,
            MinAadt = values.First(),
            MaxAadt = values.Last(),
            MedianAadt = values[values.Count / 2],
            AverageAadt = values.Average()
        };
    }

    private double CalculateDataQualityScore(QualityReport qualityReport, MatchingStatistics matchingStats)
    {
        var baseScore = 100.0;

        // Deduct points for issues
        baseScore -= qualityReport.InterstateAnomalies.Count * 10; // Critical issues
        baseScore -= qualityReport.RampToMainlineContamination.Count * 5; // High priority issues
        baseScore -= qualityReport.AadtOutliers.Count * 1; // Medium priority issues

        // Bonus for high matching rate
        if (matchingStats.MatchingRate > 80) baseScore += 5;
        if (matchingStats.MatchingRate > 90) baseScore += 5;

        return Math.Max(0, Math.Min(100, baseScore));
    }

    private async Task<string> SaveEnhancedDatasetAsync(EnhancedGeoJsonFeatureCollection featureCollection, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "parker-roads-with-enhanced-traffic.geojson");
        var jsonData = JsonSerializer.Serialize(featureCollection, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(outputPath, jsonData);

        // Also save quality report separately
        var qualityReportPath = Path.Combine(outputDirectory, "traffic-quality-report.json");
        var qualityReportJson = JsonSerializer.Serialize(featureCollection.Metadata?.QualitySummary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(qualityReportPath, qualityReportJson);

        _logger.LogInformation("📁 Enhanced dataset saved to: {OutputPath}", outputPath);
        _logger.LogInformation("📊 Quality report saved to: {QualityReportPath}", qualityReportPath);

        return outputPath;
    }

    private void DisplayComprehensiveStatistics(EnhancedGeoJsonFeatureCollection featureCollection)
    {
        var metadata = featureCollection.Metadata!;
        var stats = metadata.MatchingStatistics;

        _logger.LogInformation("📊 Enhanced Merger Summary:");
        _logger.LogInformation("   • Total roads processed: {TotalRoads}", stats.TotalRoads);
        _logger.LogInformation("   • Roads with traffic data: {MatchedRoads}", stats.MatchedRoads);
        _logger.LogInformation("   • Matching rate: {MatchingRate:F1}%", stats.MatchingRate);
        _logger.LogInformation("   • Data quality score: {QualityScore:F1}/100", metadata.QualitySummary.DataQualityScore);

        // AADT statistics by road type
        foreach (var (roadType, aadtStats) in stats.AadtByRoadType)
        {
            _logger.LogInformation("   📈 {RoadType} AADT: Min={Min}, Median={Median}, Max={Max} ({Count} roads)",
                roadType, aadtStats.MinAadt, aadtStats.MedianAadt, aadtStats.MaxAadt, aadtStats.Count);
        }

        // Warning summary
        if (metadata.QualitySummary.TotalWarnings > 0)
        {
            _logger.LogInformation("   ⚠️  Total warnings: {TotalWarnings}", metadata.QualitySummary.TotalWarnings);
        }
    }

    private string? GetPropertyValue(Dictionary<string, object?>? properties, string key)
    {
        if (properties?.TryGetValue(key, out var value) == true)
        {
            return value?.ToString();
        }
        return null;
    }
}