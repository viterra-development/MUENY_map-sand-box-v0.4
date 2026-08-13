using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TripGenProcessor.Models;
using TripGenProcessor.Services;

// ═══════════════════════════════════════════════════════════
// TripGenProcessor — Municipal Trip Generation Pipeline
// Parker County, TX
//
// CLI:
//   --parcels PATH        Input parcels GeoJSON (overrides appsettings)
//   --roads PATH          Input roads GeoJSON (overrides appsettings)
//   --boundary PATH       City boundaries GeoJSON (overrides appsettings)
//   --output-dir PATH     Directory for {city}-parcels-with-trips.geojson (overrides appsettings)
//   --city NAME           Single city to process (overrides appsettings CityName)
//   --cities "N1;N2;..."  Batch mode — process multiple cities in one run
// ═══════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  TripGenProcessor — ITE Trip Generation Pipeline");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();

// Parse CLI args
string? cliParcels = null, cliRoads = null, cliBoundary = null, cliOutDir = null;
string? cliCity = null, cliCities = null;
for (int i = 0; i < args.Length; i++)
{
    string? Val(int j) => j + 1 < args.Length ? args[j + 1] : null;
    switch (args[i])
    {
        case "--parcels":    cliParcels  = Val(i); i++; break;
        case "--roads":      cliRoads    = Val(i); i++; break;
        case "--boundary":   cliBoundary = Val(i); i++; break;
        case "--output-dir": cliOutDir   = Val(i); i++; break;
        case "--city":       cliCity     = Val(i); i++; break;
        case "--cities":     cliCities   = Val(i); i++; break;
    }
}

// Configuration
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var settings = config.GetSection("TripGenProcessor");
var parcelsPath  = cliParcels  ?? settings["InputParcelsPath"]      ?? "data/county-cad-parcels-willow-park.geojson";
var roadsPath    = cliRoads    ?? settings["InputRoadsPath"]        ?? "data/parker-roads-with-traffic.geojson";
var boundaryPath = cliBoundary ?? settings["InputCityBoundaryPath"] ?? "data/txdot-city-boundaries-filtered.geojson";
var outputDir    = cliOutDir   ?? "output";
var maxSnapDistance = double.TryParse(settings["MaxSnapDistanceMeters"], NumberStyles.Float, CultureInfo.InvariantCulture, out var sd) ? sd : 500.0;
var directionalSplit = double.TryParse(settings["DirectionalSplit"], NumberStyles.Float, CultureInfo.InvariantCulture, out var ds) ? ds : 0.50;
var defaultDu = double.TryParse(settings["DefaultDwellingUnitsPerParcel"], NumberStyles.Float, CultureInfo.InvariantCulture, out var du) ? du : 1.0;
var defaultSqft = double.TryParse(settings["DefaultSqftPerAcreFactor"], NumberStyles.Float, CultureInfo.InvariantCulture, out var sf) ? sf : 10000.0;

// Determine city list
var cities = new List<string>();
if (!string.IsNullOrWhiteSpace(cliCities))
{
    cities.AddRange(cliCities.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
else
{
    cities.Add(cliCity ?? settings["CityName"] ?? "Willow Park");
}

Directory.CreateDirectory(outputDir);

// Logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning); // reduce noise in batch mode
});
var logger = loggerFactory.CreateLogger<Program>();

// Validate inputs
if (!File.Exists(parcelsPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: Parcel data not found at: {parcelsPath}");
    Console.ResetColor();
    return 1;
}

try
{
    // ── Load parcels + roads ONCE, reuse across all cities ──
    Console.WriteLine("▶ Loading source data (shared across cities)...");
    var loader = new ParcelDataLoader(loggerFactory.CreateLogger<ParcelDataLoader>());
    var allParcels = loader.LoadParcels(parcelsPath);
    Console.WriteLine($"  Loaded {allParcels.Count:N0} total parcels");

    List<RoadSegment> roads = new();
    if (File.Exists(roadsPath))
    {
        roads = loader.LoadRoads(roadsPath);
        Console.WriteLine($"  Loaded {roads.Count:N0} road segments");
    }
    else
    {
        Console.WriteLine($"  WARN: Roads file not found at {roadsPath}; skipping road linking for all cities");
    }

    // Road linker index is built once and reused across cities
    RoadLinker? linkerCache = null;
    if (roads.Count > 0)
    {
        linkerCache = new RoadLinker(loggerFactory.CreateLogger<RoadLinker>(), maxSnapDistance);
        linkerCache.BuildIndex(roads);
    }

    Console.WriteLine();
    Console.WriteLine($"▶ Processing {cities.Count} {(cities.Count == 1 ? "city" : "cities")}: {string.Join(", ", cities)}");
    Console.WriteLine();

    var overallResults = new List<(string city, int parcels, int withTrips, double dailyTrips)>();

    foreach (var cityName in cities)
    {
        Console.WriteLine($"══ {cityName} ══");

        // Clip to boundary
        if (!File.Exists(boundaryPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ERROR: City boundary file not found at {boundaryPath} — SKIPPING {cityName}");
            Console.ResetColor();
            continue;
        }

        var boundary = loader.LoadCityBoundary(boundaryPath, cityName);
        if (boundary == null)
        {
            Console.WriteLine($"  WARN: No boundary for '{cityName}' — SKIPPING");
            continue;
        }

        var parcels = allParcels.Where(p =>
            p.Geometry != null && boundary.Contains(p.Centroid ?? p.Geometry.Centroid)
        ).ToList();
        Console.WriteLine($"  Clipped: {parcels.Count:N0} parcels within {cityName}");

        if (parcels.Count == 0)
        {
            Console.WriteLine($"  WARN: 0 parcels for {cityName} — SKIPPING output write");
            continue;
        }

        // Classify + calculate trips
        var classifier = new LandUseClassifier(loggerFactory.CreateLogger<LandUseClassifier>());
        var calculator = new TripCalculator(
            loggerFactory.CreateLogger<TripCalculator>(),
            classifier, directionalSplit, defaultDu, defaultSqft);
        var results = calculator.CalculateAll(parcels);

        // Link to roads (reset per-city aggregation state on the shared road list)
        // NOTE: We reset the aggregated fields so cities don't accumulate onto each other.
        foreach (var r in roads)
        {
            r.TotalDailyTrips = 0; r.TotalAmPeakTrips = 0; r.TotalPmPeakTrips = 0;
            r.ParcelCount = 0; r.VolumeToCapacityRatio = null;
        }
        if (linkerCache != null)
        {
            linkerCache.LinkAll(parcels, results);
            var aggregator = new TripAggregator(loggerFactory.CreateLogger<TripAggregator>());
            aggregator.AggregateToRoads(results, roads);
        }

        // Output paths — derive from city name (lowercase, hyphenated, no punctuation)
        var slug = Slugify(cityName);
        var outParcels = Path.Combine(outputDir, $"{slug}-parcels-with-trips.geojson");
        var outRoads   = Path.Combine(outputDir, $"{slug}-roads-with-trip-volumes.geojson");
        var outSummary = Path.Combine(outputDir, $"{slug}-summary.json");

        var writer = new OutputWriter(loggerFactory.CreateLogger<OutputWriter>());
        writer.WriteParcels(outParcels, parcels, results);
        if (roads.Count > 0) writer.WriteRoads(outRoads, roads);

        var summaryAgg = new TripAggregator(loggerFactory.CreateLogger<TripAggregator>());
        var summary = summaryAgg.GenerateSummary(cityName, parcels, results, roads);
        writer.WriteSummary(outSummary, summary);

        Console.WriteLine($"  → {summary.ParcelsWithTrips:N0}/{summary.TotalParcels:N0} generating {summary.TotalDailyTrips:N0} daily trips");
        Console.WriteLine($"  → {outParcels}");
        overallResults.Add((cityName, summary.TotalParcels, summary.ParcelsWithTrips, summary.TotalDailyTrips));
        Console.WriteLine();
    }

    // Final table
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine("  BATCH SUMMARY");
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine($"  {"City",-20} {"Parcels",10} {"With Trips",12} {"Daily Trips",14}");
    foreach (var (city, tp, wt, dt) in overallResults)
    {
        Console.WriteLine($"  {city,-20} {tp,10:N0} {wt,12:N0} {dt,14:N0}");
    }
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

static string Slugify(string name)
{
    // "Willow Park" -> "willow-park", "Reno (Parker)" -> "reno" (strip parenthetical),
    // "Annetta North" -> "annetta-north". Matches the historical file naming so
    // wwwroot layer URLs (/willow-park-parcels-with-trips.geojson etc.) keep working.
    var s = name.ToLowerInvariant();
    var openParen = s.IndexOf('(');
    if (openParen > 0) s = s[..openParen];
    var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
    var joined = new string(chars);
    while (joined.Contains("--")) joined = joined.Replace("--", "-");
    return joined.Trim('-');
}
