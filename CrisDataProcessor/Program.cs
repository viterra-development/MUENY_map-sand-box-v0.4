using System.Text.Json;
using CrisDataProcessor.Services;
using MapSandBox.Models;
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
                services.AddScoped<CrisProcessor>();
                services.AddScoped<CrisCsvParser>();
                services.AddScoped<CrisRiskCalculator>();
                services.AddScoped<CrisSpatialAnalyzer>();
                services.AddScoped<CrisGeoJsonGenerator>();
                services.AddScoped<RoadGeometryService>();
                services.AddScoped<EnhancedRiskSegmentGenerator>();
                services.AddScoped<EnhancedCrisSpatialAnalyzer>();
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

    public CrisProcessor(
        ILogger<CrisProcessor> logger,
        CrisCsvParser csvParser,
        CrisRiskCalculator riskCalculator,
        CrisSpatialAnalyzer spatialAnalyzer,
        CrisGeoJsonGenerator geoJsonGenerator,
        RoadGeometryService roadGeometryService,
        EnhancedRiskSegmentGenerator enhancedRiskGenerator,
        EnhancedCrisSpatialAnalyzer enhancedSpatialAnalyzer)
    {
        _logger = logger;
        _csvParser = csvParser;
        _riskCalculator = riskCalculator;
        _spatialAnalyzer = spatialAnalyzer;
        _geoJsonGenerator = geoJsonGenerator;
        _roadGeometryService = roadGeometryService;
        _enhancedRiskGenerator = enhancedRiskGenerator;
        _enhancedSpatialAnalyzer = enhancedSpatialAnalyzer;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting CRIS data processing...");

        try
        {
            // Step 1: Parse CSV files
            var crashes = await ParseCsvDataAsync();
            _logger.LogInformation("Parsed {Count} crash records", crashes.Count);

            // Step 2: Validate and filter crashes
            var validCrashes = crashes.Where(_csvParser.ValidateCrashRecord).ToList();
            _logger.LogInformation("Validated {Count} crash records (filtered {Removed})",
                validCrashes.Count, crashes.Count - validCrashes.Count);

            // Step 3: Enhanced spatial join crashes to road segments using actual road geometry
            var roadGeoJsonPath = "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-county-roads.geojson";
            var trafficRoadGeoJsonPath = "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-traffic.geojson";

            // Use enhanced spatial analyzer with road geometry service
            var spatialJoins = await _enhancedSpatialAnalyzer.SpatialJoinCrashesToRoadsAsync(validCrashes, roadGeoJsonPath);
            var crashesBySegment = _enhancedSpatialAnalyzer.GroupCrashesByRoadSegment(spatialJoins);

            // Step 4: Extract AADT data from traffic-enabled roads
            var aadtBySegment = await _spatialAnalyzer.ExtractAadtFromRoadDataAsync(trafficRoadGeoJsonPath);

            // Step 5: Generate enhanced risk segments with actual road geometry
            var riskSegments = await _enhancedRiskGenerator.GenerateEnhancedRiskSegmentsFromCrashes(
                validCrashes, spatialJoins, aadtBySegment, roadGeoJsonPath);

            // Step 6: Identify high-risk intersections
            var intersectionRisks = _spatialAnalyzer.IdentifyHighRiskIntersections(validCrashes);

            // Step 7: Generate output files
            await GenerateOutputFilesAsync(validCrashes, riskSegments, intersectionRisks, spatialJoins);

            _logger.LogInformation("CRIS data processing completed successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CRIS data processing");
            throw;
        }
    }

    private async Task<List<CrashRecord>> ParseCsvDataAsync()
    {
        var inputDir = "/workspaces/map-sand-box/CRIS Exports/extract_public_2023_20250818094137870_115143_20250101-20250818_PARKER";
        var crashes = new List<CrashRecord>();

        // CRIS export file names
        var crashCsvPath = Path.Combine(inputDir, "extract_public_2023_20250818094137_crash_20250101-20250818_PARKER.csv");
        var personCsvPath = Path.Combine(inputDir, "extract_public_2023_20250818094137_person_20250101-20250818_PARKER.csv");
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

        // Parse person.csv
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
        var outputDir = "/workspaces/map-sand-box/MapSandBox/wwwroot/cris-data";
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

        // Generate metadata
        var metadata = new CrisMetadata
        {
            GeneratedAt = DateTime.UtcNow,
            DataSource = "CRIS",
            StartDate = crashes.Any() ? DateOnly.FromDateTime(crashes.Min(c => c.CrashDateTime)) : DateOnly.FromDateTime(DateTime.Now.AddYears(-3)),
            EndDate = crashes.Any() ? DateOnly.FromDateTime(crashes.Max(c => c.CrashDateTime)) : DateOnly.FromDateTime(DateTime.Now),
            TotalCrashes = crashes.Count,
            TrafficEnabledSegments = riskSegments.Count,
            ModelWeights = new CrisModelWeights()
        };

        var metadataPath = Path.Combine(outputDir, "cris-model-metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        _logger.LogInformation("Generated metadata: {Path}", metadataPath);
    }

    private async Task CreateSampleInputStructure(string inputDir)
    {
        var sampleReadme = @"# CRIS Data Input Directory

Place your CRIS CSV export files in this directory:

- crash.csv: Main crash records
- person.csv: Person information
- unit.csv: Vehicle/unit information
- damages.csv: Damage assessments (optional)
- charges.csv: Legal charges (optional)
- lookup.csv: Reference data (optional)

The processor will read crash.csv, person.csv, and unit.csv to generate the processed GeoJSON files for the web application.

Ensure the CSV files contain the following key fields:

## crash.csv
- CrashId
- CrashDate, CrashTime
- Latitude, Longitude
- CrashSeverity
- WeatherCondition, LightCondition, SurfaceCondition

## person.csv
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
}
