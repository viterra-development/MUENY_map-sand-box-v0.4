using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TripGenProcessor.Models;
using TripGenProcessor.Services;

// ═══════════════════════════════════════════════════════════
// TripGenProcessor — Municipal Trip Generation Pipeline
// Parker County / Willow Park, TX
// Follows TCDS.Importer pattern
// ═══════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  TripGenProcessor — ITE Trip Generation Pipeline");
Console.WriteLine("  Parker County / Willow Park, TX");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();

// Configuration
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var settings = config.GetSection("TripGenProcessor");
var parcelsPath = settings["InputParcelsPath"] ?? "data/county-cad-parcels-willow-park.geojson";
var roadsPath = settings["InputRoadsPath"] ?? "data/parker-roads-with-traffic.geojson";
var boundaryPath = settings["InputCityBoundaryPath"] ?? "data/txdot-city-boundaries-filtered.geojson";
var outputParcelsPath = settings["OutputParcelsPath"] ?? "output/willow-park-parcels-with-trips.geojson";
var outputRoadsPath = settings["OutputRoadsPath"] ?? "output/parker-roads-with-trip-volumes.geojson";
var outputSummaryPath = settings["OutputSummaryPath"] ?? "output/trip-generation-summary.json";
var cityName = settings["CityName"] ?? "Willow Park";
var maxSnapDistance = double.TryParse(settings["MaxSnapDistanceMeters"], out var sd) ? sd : 500.0;
var directionalSplit = double.TryParse(settings["DirectionalSplit"], out var ds) ? ds : 0.50;
var defaultDu = double.TryParse(settings["DefaultDwellingUnitsPerParcel"], out var du) ? du : 1.0;
var defaultSqft = double.TryParse(settings["DefaultSqftPerAcreFactor"], out var sf) ? sf : 10000.0;

// Logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

// Validate inputs
if (!File.Exists(parcelsPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: Parcel data not found at: {parcelsPath}");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Expected input files:");
    Console.WriteLine($"  1. {parcelsPath}  — Parker County CAD parcels (GeoJSON)");
    Console.WriteLine($"  2. {roadsPath}  — TIGER road segments with AADT (GeoJSON)");
    Console.WriteLine($"  3. {boundaryPath}  — City boundaries for clipping (GeoJSON)");
    Console.WriteLine();
    Console.WriteLine("Data sources for Parker County parcels:");
    Console.WriteLine("  • TxGIO DataHub: https://tnris.org/stratmap/land-parcels.html");
    Console.WriteLine("  • Texas County GIS Data: https://texascountygisdata.com/product/parker-county-shapefile-and-property-data/");
    Console.WriteLine("  • Parker County CAD: https://iswdataclient.azurewebsites.net/webindex.aspx?dbkey=parkercad");
    Console.WriteLine("  • Koordinates: https://koordinates.com/layer/10039-parker-county-texas-parcels/");
    Console.WriteLine();
    Console.WriteLine("Place parcel GeoJSON in the data/ directory and re-run.");
    return 1;
}

try
{
    // ── Step 1: Load Data ──────────────────────────────────
    Console.WriteLine("▶ Step 1: Loading data...");

    var loader = new ParcelDataLoader(loggerFactory.CreateLogger<ParcelDataLoader>());

    var allParcels = loader.LoadParcels(parcelsPath);
    Console.WriteLine($"  Loaded {allParcels.Count} parcels");

    List<RoadSegment> roads;
    if (File.Exists(roadsPath))
    {
        roads = loader.LoadRoads(roadsPath);
        Console.WriteLine($"  Loaded {roads.Count} road segments");
    }
    else
    {
        logger.LogWarning("Roads file not found at {Path}. Skipping road linking.", roadsPath);
        roads = new List<RoadSegment>();
    }

    // ── Step 2: Clip to City Boundary ──────────────────────
    var parcels = allParcels;
    if (File.Exists(boundaryPath))
    {
        Console.WriteLine($"▶ Step 2: Clipping parcels to {cityName} boundary...");
        var boundary = loader.LoadCityBoundary(boundaryPath, cityName);

        if (boundary != null)
        {
            parcels = allParcels.Where(p =>
                p.Geometry != null && boundary.Contains(p.Centroid ?? p.Geometry.Centroid)
            ).ToList();
            Console.WriteLine($"  Clipped: {parcels.Count} parcels within {cityName} (from {allParcels.Count})");
        }
        else
        {
            Console.WriteLine($"  Warning: No boundary found for '{cityName}', using all parcels");
        }
    }
    else
    {
        Console.WriteLine("▶ Step 2: No city boundary file, using all parcels");
    }

    // ── Step 3: Calculate Trip Generation ──────────────────
    Console.WriteLine("▶ Step 3: Calculating trip generation...");

    var classifier = new LandUseClassifier(loggerFactory.CreateLogger<LandUseClassifier>());
    var calculator = new TripCalculator(
        loggerFactory.CreateLogger<TripCalculator>(),
        classifier,
        directionalSplit,
        defaultDu,
        defaultSqft
    );

    var results = calculator.CalculateAll(parcels);

    // ── Step 4: Link Parcels to Roads ──────────────────────
    if (roads.Count > 0)
    {
        Console.WriteLine("▶ Step 4: Linking parcels to road network...");

        var linker = new RoadLinker(loggerFactory.CreateLogger<RoadLinker>(), maxSnapDistance);
        linker.BuildIndex(roads);
        linker.LinkAll(parcels, results);
    }
    else
    {
        Console.WriteLine("▶ Step 4: Skipped (no road data)");
    }

    // ── Step 5: Aggregate Trips to Roads ───────────────────
    if (roads.Count > 0)
    {
        Console.WriteLine("▶ Step 5: Aggregating trips to road segments...");

        var aggregator = new TripAggregator(loggerFactory.CreateLogger<TripAggregator>());
        aggregator.AggregateToRoads(results, roads);
    }
    else
    {
        Console.WriteLine("▶ Step 5: Skipped (no road data)");
    }

    // ── Step 6: Write Outputs ──────────────────────────────
    Console.WriteLine("▶ Step 6: Writing output files...");

    var writer = new OutputWriter(loggerFactory.CreateLogger<OutputWriter>());
    writer.WriteParcels(outputParcelsPath, parcels, results);

    if (roads.Count > 0)
        writer.WriteRoads(outputRoadsPath, roads);

    // Generate and write summary
    var aggregatorForSummary = new TripAggregator(loggerFactory.CreateLogger<TripAggregator>());
    var summary = aggregatorForSummary.GenerateSummary(cityName, parcels, results, roads);
    writer.WriteSummary(outputSummaryPath, summary);

    // ── Print Summary ──────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine("  TRIP GENERATION SUMMARY");
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine($"  City:              {summary.CityName}");
    Console.WriteLine($"  Total Parcels:     {summary.TotalParcels:N0}");
    Console.WriteLine($"  Generating Trips:  {summary.ParcelsWithTrips:N0}");
    Console.WriteLine($"  Skipped (0-trip):  {summary.ParcelsSkipped:N0}");
    Console.WriteLine($"  ─────────────────────────────────────────────");
    Console.WriteLine($"  Daily Trips:       {summary.TotalDailyTrips:N0}");
    Console.WriteLine($"  AM Peak Trips:     {summary.TotalAmPeakTrips:N0}");
    Console.WriteLine($"  PM Peak Trips:     {summary.TotalPmPeakTrips:N0}");
    Console.WriteLine($"  Road Links:        {summary.RoadSegmentsLinked:N0}");
    Console.WriteLine();

    if (summary.ByCategory.Any())
    {
        Console.WriteLine("  BY CATEGORY:");
        foreach (var (cat, data) in summary.ByCategory.OrderByDescending(c => c.Value.DailyTrips))
        {
            Console.WriteLine($"    {cat,-15} {data.ParcelCount,6:N0} parcels  →  {data.DailyTrips,8:N0} daily trips");
        }
        Console.WriteLine();
    }

    if (summary.TopRoads.Any())
    {
        Console.WriteLine("  TOP ROADS BY GENERATED TRIPS:");
        foreach (var (_, road) in summary.TopRoads.Take(10))
        {
            var vcStr = road.VolumeToCapacityRatio.HasValue ? $"V/C: {road.VolumeToCapacityRatio:F2}" : "";
            Console.WriteLine($"    {road.RoadName,-25} {road.DailyTrips,8:N0} trips  ({road.ParcelCount} parcels)  {vcStr}");
        }
        Console.WriteLine();
    }

    if (summary.Warnings.Any())
    {
        Console.WriteLine("  WARNINGS:");
        foreach (var w in summary.Warnings)
            Console.WriteLine($"    {w}");
        Console.WriteLine();
    }

    Console.WriteLine("  OUTPUT FILES:");
    Console.WriteLine($"    {outputParcelsPath}");
    if (roads.Count > 0) Console.WriteLine($"    {outputRoadsPath}");
    Console.WriteLine($"    {outputSummaryPath}");
    Console.WriteLine("═══════════════════════════════════════════════════");

    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FATAL ERROR: {ex.Message}");
    Console.ResetColor();
    logger.LogError(ex, "TripGenProcessor failed");
    return 1;
}
