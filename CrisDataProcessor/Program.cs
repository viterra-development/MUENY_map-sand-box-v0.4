using System.Text.Json;
using CrisDataProcessor.Services;
using MapSandBox.Models;
using MapSandBox.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.Services.GetRequiredService<CrisProcessor>().RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Configure CRIS model configuration from appsettings.json
                var crisConfig = context.Configuration.GetSection("CrisModelConfiguration").Get<CrisModelConfiguration>()
                    ?? new CrisModelConfiguration();
                services.AddSingleton(crisConfig);

                services.AddScoped<CrisProcessor>();
                services.AddScoped<CrisCsvParser>();
                services.AddScoped<CrisRiskCalculator>();
                services.AddScoped<CrisSpatialAnalyzer>();
                services.AddScoped<CrisGeoJsonGenerator>();
                services.AddScoped<RoadGeometryService>();
                services.AddScoped<EnhancedRiskSegmentGenerator>();
                services.AddScoped<EnhancedCrisSpatialAnalyzer>();
                services.AddScoped<GdalCommandLineService>();
                services.AddScoped<ElevationService>();
                services.AddScoped<EnvironmentalAnalyzer>();
            });
}

public class CrisProcessor
{
    private readonly ILogger<CrisProcessor> _logger;
    private readonly CrisCsvParser _csvParser;
    private readonly CrisRiskCalculator _riskCalculator;
    private readonly CrisSpatialAnalyzer _spatialAnalyzer;
    private readonly CrisGeoJsonGenerator _geoJsonGenerator;
    private readonly RoadGeometryService _roadGeometryService;
    private readonly EnhancedRiskSegmentGenerator _enhancedRiskGenerator;
    private readonly EnhancedCrisSpatialAnalyzer _enhancedSpatialAnalyzer;
    private readonly ElevationService _elevationService;
    private readonly EnvironmentalAnalyzer _environmentalAnalyzer;
    private readonly CrisModelConfiguration _config;

    public CrisProcessor(
        ILogger<CrisProcessor> logger,
        CrisCsvParser csvParser,
        CrisRiskCalculator riskCalculator,
        CrisSpatialAnalyzer spatialAnalyzer,
        CrisGeoJsonGenerator geoJsonGenerator,
        RoadGeometryService roadGeometryService,
        EnhancedRiskSegmentGenerator enhancedRiskGenerator,
        EnhancedCrisSpatialAnalyzer enhancedSpatialAnalyzer,
        ElevationService elevationService,
        EnvironmentalAnalyzer environmentalAnalyzer,
        CrisModelConfiguration config)
    {
        _logger = logger;
        _csvParser = csvParser;
        _riskCalculator = riskCalculator;
        _spatialAnalyzer = spatialAnalyzer;
        _geoJsonGenerator = geoJsonGenerator;
        _roadGeometryService = roadGeometryService;
        _enhancedRiskGenerator = enhancedRiskGenerator;
        _enhancedSpatialAnalyzer = enhancedSpatialAnalyzer;
        _elevationService = elevationService;
        _environmentalAnalyzer = environmentalAnalyzer;
        _config = config;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting CRIS data processing with configuration:");
        _logger.LogInformation("Model weights: CF={CrashFreq}, SI={SeverityIndex}, TV={TrafficVolume}, DR={DrainageRisk}, ENV={Environmental}",
            _config.ModelWeights.CrashFrequency, _config.ModelWeights.SeverityIndex,
            _config.ModelWeights.TrafficVolume, _config.ModelWeights.DrainageRisk, _config.ModelWeights.Environmental);
        _logger.LogInformation("Thresholds: CrashFreq={CrashFreq}/mile, Traffic={Traffic} AADT, Slope={Slope}%",
            _config.Thresholds.CrashFrequencyPerMile, _config.Thresholds.TrafficVolumeThreshold, _config.Thresholds.SlopeThreshold);

        try
        {
            // Step 1: Parse CSV files using configured paths
            var crashes = await ParseCsvDataAsync();
            _logger.LogInformation("Parsed {Count} crash records", crashes.Count);

            // Step 2: Validate and filter crashes
            var validCrashes = crashes.Where(_csvParser.ValidateCrashRecord).ToList();
            _logger.LogInformation("Validated {Count} crash records (filtered {Removed})",
                validCrashes.Count, crashes.Count - validCrashes.Count);

            // Step 3: Enhanced spatial join crashes to road segments using enhanced traffic roads
            var roadGeoJsonPath = _config.DataSources.RoadNetworkPath;
            var trafficRoadGeoJsonPath = _config.DataSources.TrafficRoadsPath;

            // Use enhanced traffic roads for MULTI-ASSOCIATION spatial join to ensure matching LINEARID keys
            var spatialJoins = await _enhancedSpatialAnalyzer.SpatialJoinCrashesToRoadsMultiAsync(validCrashes, trafficRoadGeoJsonPath);
            var crashesBySegment = _enhancedSpatialAnalyzer.GroupCrashesByRoadSegment(spatialJoins);
            var enhancedCrashesBySegment = _enhancedSpatialAnalyzer.GroupCrashesByRoadSegmentEnhanced(spatialJoins);

            // Log multi-association statistics
            LogMultiAssociationStatistics(enhancedCrashesBySegment);

            // Step 4: Extract AADT data from traffic-enabled roads
            var aadtBySegment = await _spatialAnalyzer.ExtractAadtFromRoadDataAsync(trafficRoadGeoJsonPath);

            // Step 5: Generate enhanced risk segments with actual road geometry from traffic roads
            var riskSegments = await _enhancedRiskGenerator.GenerateEnhancedRiskSegmentsFromCrashes(
                validCrashes, spatialJoins, aadtBySegment, trafficRoadGeoJsonPath);

            // Step 6: Enhanced crash records with DEM slope data (first - needed for crash-based segment analysis)
            await _elevationService.EnhanceCrashRecordsWithDEMSlope(validCrashes);

            // Step 6.1: Enhanced elevation/slope analysis using crash-based slope data
            await _elevationService.EnhanceRoadSegmentsWithCrashBasedSlope(riskSegments, validCrashes, crashesBySegment);

            // Step 7: Enhanced environmental analysis
            _environmentalAnalyzer.EnhanceSegmentsWithEnvironmentalAnalysis(riskSegments, crashesBySegment);

            // Step 8: Calculate detailed risk scores using configured weights and thresholds
            foreach (var segment in riskSegments)
            {
                var segmentCrashes = crashesBySegment.GetValueOrDefault(segment.SegmentId, new List<CrashRecord>());
                var detailedScore = _riskCalculator.CalculateDetailedRiskScore(segmentCrashes, segment);

                // Update segment with detailed metrics and threshold flags
                segment.CrashesPerMilePerYear = detailedScore.CrashFrequencyPerMile;
                segment.FatalCrashCount = detailedScore.FatalCrashes;
                segment.SeriousInjuryCrashCount = detailedScore.IncapacitatingInjuryCrashes;

                // Store component scores for potential future real-time recalculation
                segment.CrashFrequencyScore = detailedScore.CrashFrequencyScore;
                segment.SeverityIndexScore = detailedScore.SeverityIndexScore;
                segment.TrafficVolumeScore = detailedScore.TrafficVolumeScore;
                segment.DrainageRiskScore = detailedScore.DrainageRiskScore;
                segment.EnvironmentalScore = detailedScore.EnvironmentalScore;

                // Update final scores using configured weights
                segment.RiskScore = detailedScore.CompositeRiskScore;
                segment.RiskLevel = detailedScore.RiskLevel;

                // Apply configured thresholds
                segment.MeetsCrashFrequencyThreshold = detailedScore.CrashFrequencyPerMile > _config.Thresholds.CrashFrequencyPerMile;
                segment.MeetsSeverityThreshold = detailedScore.FatalCrashes >= _config.Thresholds.FatalCrashThreshold ||
                                               detailedScore.IncapacitatingInjuryCrashes >= _config.Thresholds.IncapacitatingInjuryThreshold;
                segment.MeetsTrafficVolumeThreshold = segment.Aadt > _config.Thresholds.TrafficVolumeThreshold;
                segment.HasDrainageRisk = segment.SlopePercentage > _config.Thresholds.SlopeThreshold ||
                                        segment.EnvironmentalFactors.HydroplaningIncidents > 0;
                segment.HasEnvironmentalRisk = segment.EnvironmentalFactors.WetSurfaceCrashes +
                                             segment.EnvironmentalFactors.IcySurfaceCrashes > segmentCrashes.Count * 0.2m;
            }

            // Step 9: Identify high-risk intersections
            var intersectionRisks = _spatialAnalyzer.IdentifyHighRiskIntersections(validCrashes);

            // Step 10: Generate statistics and summaries
            var elevationStats = _elevationService.CalculateElevationStatistics(riskSegments);
            var environmentalSummary = _environmentalAnalyzer.GenerateEnvironmentalRiskSummary(riskSegments);

            // Step 11: Generate output files with enhanced data
            await GenerateOutputFilesAsync(validCrashes, riskSegments, intersectionRisks, spatialJoins);

            // Log processing summary
            _logger.LogInformation("CRIS data processing completed successfully!");
            _logger.LogInformation("Generated {SegmentCount} risk segments using configured weights and thresholds", riskSegments.Count);
            _logger.LogInformation("High-risk segments: {HighRiskCount}, Drainage issues: {DrainageCount}, Weather-sensitive: {WeatherCount}",
                riskSegments.Count(s => s.RiskLevel is RiskLevel.High or RiskLevel.VeryHigh),
                environmentalSummary.SegmentsWithDrainageIssues,
                environmentalSummary.WeatherSensitiveSegments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CRIS data processing");
            throw;
        }
    }

    private async Task<List<CrashRecord>> ParseCsvDataAsync()
    {
        var inputDir = _config.DataSources.InputDirectory;
        var crashes = new List<CrashRecord>();

        // CRIS export file names
        var crashCsvPath = Path.Combine(inputDir, "extract_public_2023_20250818094137_crash_20250101-20250818_PARKER.csv");
        var personCsvPath = Path.Combine(inputDir, "extract_public_2023_20250818094137_primaryperson_20250101-20250818_PARKER.csv");
        var unitCsvPath = Path.Combine(inputDir, "extract_public_2023_20250818094137_unit_20250101-20250818_PARKER.csv");

        if (!File.Exists(crashCsvPath))
        {
            _logger.LogWarning("Required CRIS crash CSV file not found: {CrashPath}", crashCsvPath);
            await CreateSampleInputStructure(inputDir);
            return crashes;
        }

        _logger.LogInformation("Found CRIS data files in {InputDir}", inputDir);

        // Parse crash.csv (already checked that it exists)
        var crashRecords = await _csvParser.ParseCrashCsvAsync(crashCsvPath);

        // Parse primaryperson.csv (contains complete person records)
        var personRecords = File.Exists(personCsvPath)
            ? await _csvParser.ParsePersonCsvAsync(personCsvPath)
            : new List<PersonCsvRecord>();

        // Parse unit.csv
        var unitRecords = File.Exists(unitCsvPath)
            ? await _csvParser.ParseUnitCsvAsync(unitCsvPath)
            : new List<UnitCsvRecord>();

        // Convert to crash records
        crashes = crashRecords
            .Select(c => _csvParser.ConvertToCrashRecord(c, personRecords, unitRecords))
            .ToList();

        return crashes;
    }

    private async Task GenerateOutputFilesAsync(
        List<CrashRecord> crashes,
        List<RiskSegment> riskSegments,
        List<IntersectionRisk> intersectionRisks,
        List<EnhancedSpatialJoinResult> spatialJoins)
    {
        var outputDir = _config.DataSources.OutputDirectory;
        Directory.CreateDirectory(outputDir);

        // Generate crash points GeoJSON (only crashes matched to traffic-enabled roads)
        var trafficCrashes = spatialJoins.Where(s => s.WithinThreshold).Select(s => s.Crash).ToList();
        var crashGeoJson = _geoJsonGenerator.GenerateCrashPointsGeoJson(trafficCrashes);
        var crashOutputPath = Path.Combine(outputDir, "parker-county-crashes-traffic-roads.geojson");
        await File.WriteAllTextAsync(crashOutputPath, JsonSerializer.Serialize(crashGeoJson, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated crash points GeoJSON: {Path}", crashOutputPath);

        // Generate crash points deck.gl format
        var crashDeckGlData = _geoJsonGenerator.GenerateCrashPointsDeckGlData(trafficCrashes);
        var crashDeckGlOutputPath = Path.Combine(outputDir, "parker-county-crashes-traffic-roads-deckgl.json");
        await File.WriteAllTextAsync(crashDeckGlOutputPath, JsonSerializer.Serialize(crashDeckGlData, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated crash points deck.gl format: {Path}", crashDeckGlOutputPath);

        // Generate UNIQUE crash points deck.gl format (deduplicated for spatial visualization)
        var uniqueTrafficCrashes = trafficCrashes
            .GroupBy(c => c.CrashId)
            .Select(g => g.First()) // Take first occurrence of each unique crash ID
            .ToList();
        var uniqueCrashDeckGlData = _geoJsonGenerator.GenerateCrashPointsDeckGlData(uniqueTrafficCrashes);
        var uniqueCrashDeckGlOutputPath = Path.Combine(outputDir, "parker-county-crashes-unique-deckgl.json");
        await File.WriteAllTextAsync(uniqueCrashDeckGlOutputPath, JsonSerializer.Serialize(uniqueCrashDeckGlData, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated UNIQUE crash points deck.gl format: {Path} ({UniqueCount} unique from {TotalCount} total)",
            uniqueCrashDeckGlOutputPath, uniqueTrafficCrashes.Count, trafficCrashes.Count);

        // Generate TOLERANCE-BASED CLUSTERED crash points (cluster crashes within ~5 meters of each other)
        const decimal tolerance = 0.00005m; // Roughly 5 meter tolerance
        var clusteredCrashes = new List<object>();
        var processedCrashes = new HashSet<string>();

        foreach (var crash in uniqueTrafficCrashes)
        {
            if (processedCrashes.Contains(crash.CrashId))
                continue;

            // Find all crashes within tolerance of this crash
            var nearbyCluster = uniqueTrafficCrashes
                .Where(c => !processedCrashes.Contains(c.CrashId) &&
                           Math.Abs(c.Longitude - crash.Longitude) <= tolerance &&
                           Math.Abs(c.Latitude - crash.Latitude) <= tolerance)
                .ToList();

            // Mark all crashes in this cluster as processed
            foreach (var clusterCrash in nearbyCluster)
            {
                processedCrashes.Add(clusterCrash.CrashId);
            }

            var maxSeverityWeight = nearbyCluster.Max(c => GetSeverityWeight(c.Severity));
            var maxSeverityCode = ConvertSeverityToCode(nearbyCluster.First(c => GetSeverityWeight(c.Severity) == maxSeverityWeight).Severity);

            // Use centroid of cluster for position
            var avgLongitude = nearbyCluster.Average(c => c.Longitude);
            var avgLatitude = nearbyCluster.Average(c => c.Latitude);

            var cluster = new
            {
                ClusterId = $"cluster_{avgLongitude:F6}_{avgLatitude:F6}".Replace(".", "").Replace("-", "neg"),
                Position = new[] { avgLongitude, avgLatitude },
                CrashCount = nearbyCluster.Count,
                MaxSeverity = maxSeverityCode,
                TotalPersonsInvolved = nearbyCluster.Sum(c => c.Persons.Count),
                TotalVehiclesInvolved = nearbyCluster.Sum(c => c.Vehicles.Count),
                Crashes = nearbyCluster.Select(c => new CrashSummaryDeckGl
                {
                    CrashId = c.CrashId,
                    CrashDate = c.CrashDateTime.ToString("yyyy-MM-dd"),
                    Severity = c.Severity.ToString(),
                    SeverityCode = ConvertSeverityToCode(c.Severity),
                    PersonsInvolved = c.Persons.Count,
                    VehiclesInvolved = c.Vehicles.Count,
                    FatalCount = c.Persons.Count(p => p.InjurySeverity == KabcoSeverity.K_Fatal),
                    InjuryCount = c.Persons.Count(p => p.InjurySeverity != KabcoSeverity.K_Fatal && p.InjurySeverity != KabcoSeverity.O_NoInjury),
                    SlopeAtLocation = c.SlopeAtLocation,
                    SlopePercentage = c.SlopePercentage,
                    SlopeCategory = c.SlopeCategory
                }).ToList()
            };

            clusteredCrashes.Add(cluster);
        }

        var clusteredCrashOutputPath = Path.Combine(outputDir, "parker-county-crashes-clustered-deckgl.json");
        await File.WriteAllTextAsync(clusteredCrashOutputPath, JsonSerializer.Serialize(clusteredCrashes, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated CLUSTERED crash points (exact coordinates): {Path} ({ClusterCount} clusters from {UniqueCount} unique crashes)",
            clusteredCrashOutputPath, clusteredCrashes.Count, uniqueTrafficCrashes.Count);

        // Generate risk segments GeoJSON
        var riskGeoJson = _geoJsonGenerator.GenerateRiskSegmentsGeoJson(riskSegments);
        var riskOutputPath = Path.Combine(outputDir, "parker-county-risk-segments-traffic.geojson");
        await File.WriteAllTextAsync(riskOutputPath, JsonSerializer.Serialize(riskGeoJson, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated risk segments GeoJSON: {Path}", riskOutputPath);

        // Generate risk segments deck.gl format
        var riskDeckGlData = _geoJsonGenerator.GenerateRiskSegmentsDeckGlData(riskSegments);
        var riskDeckGlOutputPath = Path.Combine(outputDir, "parker-county-risk-segments-traffic-deckgl.json");
        await File.WriteAllTextAsync(riskDeckGlOutputPath, JsonSerializer.Serialize(riskDeckGlData, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated risk segments deck.gl format: {Path}", riskDeckGlOutputPath);

        // Generate intersection risks GeoJSON
        var intersectionGeoJson = _geoJsonGenerator.GenerateIntersectionRisksGeoJson(intersectionRisks);
        var intersectionOutputPath = Path.Combine(outputDir, "parker-county-intersection-risks.geojson");
        await File.WriteAllTextAsync(intersectionOutputPath, JsonSerializer.Serialize(intersectionGeoJson, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated intersection risks GeoJSON: {Path}", intersectionOutputPath);

        // Generate intersection risks deck.gl format
        var intersectionDeckGlData = _geoJsonGenerator.GenerateIntersectionRisksDeckGlData(intersectionRisks);
        var intersectionDeckGlOutputPath = Path.Combine(outputDir, "parker-county-intersection-risks-deckgl.json");
        await File.WriteAllTextAsync(intersectionDeckGlOutputPath, JsonSerializer.Serialize(intersectionDeckGlData, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated intersection risks deck.gl format: {Path}", intersectionDeckGlOutputPath);

        // Generate metadata with configured weights
        var metadata = new CrisMetadata
        {
            GeneratedAt = DateTime.UtcNow,
            DataSource = "CRIS",
            StartDate = crashes.Any() ? DateOnly.FromDateTime(crashes.Min(c => c.CrashDateTime)) : DateOnly.FromDateTime(DateTime.Now.AddYears(-3)),
            EndDate = crashes.Any() ? DateOnly.FromDateTime(crashes.Max(c => c.CrashDateTime)) : DateOnly.FromDateTime(DateTime.Now),
            TotalCrashes = crashes.Count,
            TrafficEnabledSegments = riskSegments.Count,
            ModelWeights = _config.ModelWeights
        };

        var metadataPath = Path.Combine(outputDir, "cris-model-metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated metadata: {Path}", metadataPath);
    }

    private static int GetSeverityWeight(KabcoSeverity severity)
    {
        return severity switch
        {
            KabcoSeverity.K_Fatal => 5,
            KabcoSeverity.A_IncapacitatingInjury => 4,
            KabcoSeverity.B_NonIncapacitatingInjury => 3,
            KabcoSeverity.C_PossibleInjury => 2,
            KabcoSeverity.O_NoInjury => 1,
            _ => 1 // Default to lowest severity
        };
    }

    private static string ConvertSeverityToCode(KabcoSeverity severity)
    {
        return severity switch
        {
            KabcoSeverity.K_Fatal => "K",
            KabcoSeverity.A_IncapacitatingInjury => "A",
            KabcoSeverity.B_NonIncapacitatingInjury => "B",
            KabcoSeverity.C_PossibleInjury => "C",
            KabcoSeverity.O_NoInjury => "O",
            _ => "O" // Default to no injury
        };
    }

    private async Task CreateSampleInputStructure(string inputDir)
    {
        var sampleReadme = @"# CRIS Data Input Directory

Place your CRIS CSV export files in this directory:

- crash.csv: Main crash records
- primaryperson.csv: Primary person information (drivers and key persons)
- unit.csv: Vehicle/unit information
- damages.csv: Damage assessments (optional)
- charges.csv: Legal charges (optional)
- lookup.csv: Reference data (optional)

The processor will read crash.csv, primaryperson.csv, and unit.csv to generate the processed GeoJSON files for the web application.

Ensure the CSV files contain the following key fields:

## crash.csv
- CrashId
- CrashDate, CrashTime
- Latitude, Longitude
- CrashSeverity
- WeatherCondition, LightCondition, SurfaceCondition

## primaryperson.csv
- CrashId, PersonId
- InjurySeverity
- Age, Gender

## unit.csv
- CrashId, UnitId
- VehicleType
- TravelDirection
";

        await File.WriteAllTextAsync(Path.Combine(inputDir, "README.md"), sampleReadme);
        _logger.LogInformation("Created sample input structure at {InputDir}", inputDir);
    }

    private void LogMultiAssociationStatistics(Dictionary<string, SegmentCrashSummary> enhancedCrashesBySegment)
    {
        _logger.LogInformation("=== MULTI-ASSOCIATION STATISTICS ===");

        var segmentsWithMultiAssoc = enhancedCrashesBySegment.Values.Where(s => s.HasMultiAssociations).ToList();
        var totalCrashAssociations = enhancedCrashesBySegment.Values.Sum(s => s.CrashCount);
        var totalUniqueCrashes = enhancedCrashesBySegment.Values.Sum(s => s.UniqueCrashCount);

        _logger.LogInformation("Road segments with crashes: {TotalSegments}", enhancedCrashesBySegment.Count);
        _logger.LogInformation("Segments with multi-associations: {MultiSegments}", segmentsWithMultiAssoc.Count);
        _logger.LogInformation("Total crash-to-segment associations: {TotalAssociations}", totalCrashAssociations);
        _logger.LogInformation("Total unique crashes: {UniqueCrashes}", totalUniqueCrashes);
        _logger.LogInformation("Multi-association ratio: {Ratio:F2}x", totalUniqueCrashes > 0 ? (double)totalCrashAssociations / totalUniqueCrashes : 0);

        // Log target segments specifically
        var targetSegments = enhancedCrashesBySegment.Where(kvp =>
            kvp.Key == "1103701322105" || kvp.Key == "1102970398995").ToList();

        if (targetSegments.Any())
        {
            _logger.LogInformation("=== TARGET SEGMENTS (1103701322105 & 1102970398995) ===");
            foreach (var (segmentId, summary) in targetSegments)
            {
                _logger.LogInformation("Segment {SegmentId} ({RoadName}):", segmentId, summary.RoadName);
                _logger.LogInformation("  - Total crash associations: {CrashCount}", summary.CrashCount);
                _logger.LogInformation("  - Unique crashes: {UniqueCrashes}", summary.UniqueCrashCount);
                _logger.LogInformation("  - Has multi-associations: {HasMulti}", summary.HasMultiAssociations);
                _logger.LogInformation("  - Road type: {RoadType}", summary.RoadType);

                if (summary.HasMultiAssociations)
                {
                    _logger.LogInformation("  - Multi-associated crashes: {MultiCount}", summary.MultiAssociationCount);
                }
            }
        }
    }
}
