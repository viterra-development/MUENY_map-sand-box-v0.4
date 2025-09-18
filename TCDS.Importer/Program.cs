using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using TCDS.Importer.Services;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);

// Look for appsettings.json in the solution root by searching for .sln file
var currentDir = Directory.GetCurrentDirectory();
var solutionRoot = currentDir;

// Walk up the directory tree to find the solution root (where .sln file exists)
while (!File.Exists(Path.Combine(solutionRoot, "MapSandBox.sln")) && Directory.GetParent(solutionRoot) != null)
{
    solutionRoot = Directory.GetParent(solutionRoot)!.FullName;
}

var appSettingsPath = Path.Combine(solutionRoot, "appsettings.json");
builder.Configuration.AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true);

builder.Services.Configure<TcdsConfiguration>(
    builder.Configuration.GetSection("TcdsConfiguration"));

builder.Services.AddTransient<TcdsScrapingService>();
builder.Services.AddTransient<SimpleTileGenerator>();
builder.Services.AddTransient<RoadTrafficMerger>();
builder.Services.AddTransient<AadtValidationService>();
builder.Services.AddTransient<TypeBasedTrafficMatcher>();
builder.Services.AddTransient<EnhancedRoadTrafficMerger>();
builder.Services.AddTransient<DataQualityMonitor>();

builder.Logging.AddConsole();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var tileGenerator = host.Services.GetRequiredService<SimpleTileGenerator>();

// Check if running in enhanced merge mode (our new I-20 fix)
var enhancedMergeMode = args.Contains("--enhanced-merge");

if (enhancedMergeMode)
{
    return await RunEnhancedMergeMode(host, logger, solutionRoot);
}

// Check if running in road-traffic merge mode
var mergeRoadsMode = args.Contains("--merge-roads");

if (mergeRoadsMode)
{
    logger.LogInformation("🚀 Using ENHANCED merger with I-20 fix");
    return await RunEnhancedMergeMode(host, logger, solutionRoot);
}

// Check if running in tiles-only mode
var tilesOnlyMode = args.Contains("--tiles-only");

if (tilesOnlyMode)
{
    return await RunTilesOnlyMode(host, logger, tileGenerator, builder.Configuration);
}

// Check if running in meta-runner mode
var metaRunnerMode = args.Contains("--meta-runner");

if (metaRunnerMode)
{
    return await RunMetaRunnerMode(host, logger, tileGenerator, builder.Configuration, args);
}

// Parse MinPage and MaxPage arguments
int minPage = 1;
int maxPages = 100;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--min-page" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedMinPage))
    {
        minPage = Math.Max(1, parsedMinPage);
    }
    else if (args[i] == "--max-page" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedMaxPage))
    {
        maxPages = Math.Max(1, parsedMaxPage);
    }
}

// Validate page range
if (minPage > maxPages)
{
    logger.LogError("MinPage ({MinPage}) cannot be greater than MaxPage ({MaxPage})", minPage, maxPages);
    return 1;
}

logger.LogInformation("Page range configured: {MinPage} to {MaxPage} (total: {PageCount} pages)", 
    minPage, maxPages, maxPages - minPage + 1);

var scrapingService = host.Services.GetRequiredService<TcdsScrapingService>();

try
{
    logger.LogInformation("Starting TCDS Importer application");
    
    var config = builder.Configuration.GetSection("TcdsConfiguration").Get<TcdsConfiguration>();
    if (config == null)
    {
        logger.LogError("Failed to load TcdsConfiguration from appsettings.json");
        return 1;
    }

    logger.LogInformation("Navigating to TCDS website: {Url}", config.TargetUrl);
    
    var pageData = await scrapingService.NavigateToPageAsync(config.TargetUrl);
    
    logger.LogInformation("Successfully loaded page: {Title}", pageData.Title);
    logger.LogInformation("Page content length: {Length} characters", pageData.Content.Length);
    
    logger.LogInformation("Taking initial screenshot...");
    var initialScreenshotPath = await scrapingService.TakeScreenshotAsync();
    logger.LogInformation("Initial screenshot saved to: {Path}", initialScreenshotPath);
    
    logger.LogInformation("Performing Parker County search...");
    var searchResults = await scrapingService.SearchParkerCountyAsync();
    
    logger.LogInformation("Search completed. Results page title: {Title}", searchResults.Title);
    logger.LogInformation("Search metadata: {Metadata}", string.Join(", ", searchResults.Metadata.Select(kv => $"{kv.Key}={kv.Value}")));
    
    logger.LogInformation("Taking final screenshot after search...");
    var finalScreenshotPath = await scrapingService.TakeScreenshotAsync();
    logger.LogInformation("Final screenshot saved to: {Path}", finalScreenshotPath);
    
    var allTrafficData = new List<TrafficCountData>();
    
    // Navigate to the starting page using Goto Record if not page 1
    if (minPage > 1)
    {
        logger.LogInformation("Navigating directly to starting record {MinPage} using Goto Record...", minPage);
        try
        {
            await scrapingService.GotoRecordAsync(minPage);
            logger.LogInformation("Successfully navigated to starting record {MinPage}", minPage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to navigate to starting record {MinPage} using Goto Record", minPage);
            return 1;
        }
    }
    
    logger.LogInformation("Starting data extraction loop for pages {MinPage} to {MaxPages} (total: {PageCount} pages)", 
        minPage, maxPages, maxPages - minPage + 1);
    
    for (int pageNumber = minPage; pageNumber <= maxPages; pageNumber++)
    {
        logger.LogInformation("Processing page {PageNumber} of {MaxPages}...", pageNumber, maxPages);
        
        // Extract data from current page
        logger.LogInformation("Extracting structured traffic count data from page {PageNumber}...", pageNumber);
        var trafficData = await scrapingService.ExtractTrafficDataAsync();
        
        // Save individual page data
        await SaveTrafficDataAsync(config, trafficData, $"page_{pageNumber}", logger);
        allTrafficData.Add(trafficData);
        
        logger.LogInformation("Page {PageNumber} data summary:", pageNumber);
        LogTrafficDataSummary(trafficData, logger);
        
        // Take screenshot of current page
        logger.LogInformation("Taking screenshot of page {PageNumber}...", pageNumber);
        var screenshotPath = await scrapingService.TakeScreenshotAsync();
        logger.LogInformation("Page {PageNumber} screenshot saved to: {Path}", pageNumber, screenshotPath);
        
        // Try to navigate to next page (except on the last allowed page)
        if (pageNumber < maxPages)
        {
            logger.LogInformation("Attempting to navigate to page {NextPage}...", pageNumber + 1);
            
            try
            {
                var nextPageData = await scrapingService.ClickNextRecordAsync();
                logger.LogInformation("Successfully navigated to page {NextPage}: {Title}", pageNumber + 1, nextPageData.Title);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not navigate to page {NextPage}. Reached end of available records at page {CurrentPage}", pageNumber + 1, pageNumber);
                break;
            }
        }
    }
    
    logger.LogInformation("Data extraction completed. Processed {PageCount} pages with {TotalRecords} total records", 
        allTrafficData.Count, allTrafficData.Count);
    
    // Save consolidated data
    await SaveConsolidatedDataAsync(config, allTrafficData, logger, minPage, maxPages);
    
    // Export as GeoJSON for mapping applications
    await ExportGeoJsonAsync(config, allTrafficData, logger, minPage, maxPages);
    
    // Generate spatial tiles for progressive loading
    await ExportTiledGeoJsonAsync(config, allTrafficData, tileGenerator, logger);
    
    logger.LogInformation("TCDS Importer completed successfully - {PageCount} pages ({MinPage} to {MaxPages}) of Parker County data extracted and saved", allTrafficData.Count, minPage, maxPages);
    
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred while running TCDS Importer");
    return 1;
}
finally
{
    await scrapingService.DisposeAsync();
}

static async Task SaveTrafficDataAsync(TcdsConfiguration config, TrafficCountData trafficData, string pageIdentifier, ILogger logger)
{
    // Create data directory if it doesn't exist
    Directory.CreateDirectory(config.DataDirectory);
    
    // Save extracted data as JSON
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var jsonFileName = $"parker_county_traffic_data_{pageIdentifier}_{timestamp}.json";
    var jsonFilePath = Path.Combine(config.DataDirectory, jsonFileName);
    
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    var jsonData = JsonSerializer.Serialize(trafficData, jsonOptions);
    await File.WriteAllTextAsync(jsonFilePath, jsonData);
    
    logger.LogInformation("Traffic data saved to: {Path}", jsonFilePath);
}

static async Task SaveConsolidatedDataAsync(TcdsConfiguration config, List<TrafficCountData> allTrafficData, ILogger logger, int minPage = 1, int maxPage = 100)
{
    // Create data directory if it doesn't exist
    Directory.CreateDirectory(config.DataDirectory);
    
    // Save batch-specific consolidated data as JSON
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var pageRangeSuffix = minPage == maxPage ? $"page_{minPage}" : $"pages_{minPage}_to_{maxPage}";
    var jsonFileName = $"parker_county_traffic_data_consolidated_{pageRangeSuffix}_{timestamp}.json";
    var jsonFilePath = Path.Combine(config.DataDirectory, jsonFileName);
    
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    var consolidatedData = new
    {
        ExtractedAt = DateTime.UtcNow,
        TotalRecords = allTrafficData.Count,
        PageRange = new { MinPage = minPage, MaxPage = maxPage },
        Records = allTrafficData
    };
    
    var jsonData = JsonSerializer.Serialize(consolidatedData, jsonOptions);
    await File.WriteAllTextAsync(jsonFilePath, jsonData);
    
    logger.LogInformation("Consolidated traffic data saved to: {Path}", jsonFilePath);
    
    // Update master consolidated file
    await UpdateMasterConsolidatedDataAsync(config, allTrafficData, logger, minPage, maxPage);
}

static async Task ExportGeoJsonAsync(TcdsConfiguration config, List<TrafficCountData> allTrafficData, ILogger logger, int minPage = 1, int maxPage = 100)
{
    try
    {
        logger.LogInformation("Creating GeoJSON export from {RecordCount} traffic records", allTrafficData.Count);
        
        // Create data directory if it doesn't exist
        Directory.CreateDirectory(config.DataDirectory);
        
        // Filter records that have valid coordinates
        var validRecords = allTrafficData
            .Where(r => r.LocationInfo.Latitude.HasValue && r.LocationInfo.Longitude.HasValue)
            .ToList();
        
        logger.LogInformation("Found {ValidCount} records with valid coordinates out of {TotalCount} total records", 
            validRecords.Count, allTrafficData.Count);
        
        // Create GeoJSON structure
        var features = validRecords.Select(record =>
        {
            // Get latest AADT value
            var latestAadt = record.AadtData
                .Where(a => a.Aadt.HasValue)
                .OrderByDescending(a => a.Year)
                .FirstOrDefault();
            
            // Get latest volume count
            var latestVolumeCount = record.VolumeCountData
                .OrderByDescending(v => v.Date)
                .FirstOrDefault();
            
            return new
            {
                type = "Feature",
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] 
                    { 
                        (double)record.LocationInfo.Longitude!.Value, 
                        (double)record.LocationInfo.Latitude!.Value 
                    }
                },
                properties = new
                {
                    locationId = record.LocationId,
                    locatedOn = record.LocationInfo.LocatedOn,
                    locOnAlias = record.LocationInfo.LocOnAlias,
                    type = record.LocationInfo.Type,
                    category = record.LocationInfo.Category,
                    routeType = record.LocationInfo.RouteType,
                    route = record.LocationInfo.Route,
                    active = record.LocationInfo.Active,
                    fnctClass = record.LocationInfo.FnctClass,
                    
                    // Latest AADT data
                    latestAadt = latestAadt?.Aadt,
                    latestAadtYear = latestAadt?.Year,
                    latestDhv30 = latestAadt?.Dhv30,
                    latestKPercent = latestAadt?.KPercent,
                    latestDPercent = latestAadt?.DPercent,
                    
                    // Latest volume count
                    latestVolumeCount = latestVolumeCount?.Total,
                    latestVolumeDate = latestVolumeCount?.Date.ToString("yyyy-MM-dd"),
                    
                    // Counts for reference
                    totalAadtRecords = record.AadtData.Count,
                    totalVolumeRecords = record.VolumeCountData.Count,
                    totalTrendRecords = record.VolumeTrendData.Count,
                    
                    // Processing metadata
                    extractedAt = record.ExtractedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            };
        }).ToList();
        
        var geoJson = new
        {
            type = "FeatureCollection",
            metadata = new
            {
                title = "Parker County Traffic Count Data",
                description = "Traffic count locations with latest AADT values from TCDS",
                totalFeatures = features.Count,
                pageRange = new { minPage, maxPage },
                exportedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                source = "Texas Department of Transportation TCDS",
                county = "Parker County, Texas"
            },
            features = features
        };
        
        // Save GeoJSON file
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var pageRangeSuffix = minPage == maxPage ? $"page_{minPage}" : $"pages_{minPage}_to_{maxPage}";
        var geoJsonFileName = $"parker_county_traffic_locations_{pageRangeSuffix}_{timestamp}.geojson";
        var geoJsonFilePath = Path.Combine(config.DataDirectory, geoJsonFileName);
        
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var geoJsonData = JsonSerializer.Serialize(geoJson, jsonOptions);
        await File.WriteAllTextAsync(geoJsonFilePath, geoJsonData);
        
        logger.LogInformation("GeoJSON export completed successfully");
        logger.LogInformation("- Output file: {Path}", geoJsonFilePath);
        logger.LogInformation("- Features exported: {FeatureCount}", features.Count);
        logger.LogInformation("- Records with AADT data: {AadtCount}", features.Count(f => f.properties.latestAadt != null));
        logger.LogInformation("- Records with volume data: {VolumeCount}", features.Count(f => f.properties.latestVolumeCount != null));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to export GeoJSON");
    }
}

static async Task ExportTiledGeoJsonAsync(TcdsConfiguration config, List<TrafficCountData> allTrafficData, SimpleTileGenerator tileGenerator, ILogger logger)
{
    try
    {
        logger.LogInformation("Generating spatial tiles for progressive loading");
        
        await tileGenerator.GenerateTiles(allTrafficData, config.DataDirectory);
        
        logger.LogInformation("Spatial tiles generated successfully in {Directory}", Path.Combine(config.DataDirectory, "tiles"));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate spatial tiles");
    }
}

static void LogTrafficDataSummary(TrafficCountData trafficData, ILogger logger)
{
    logger.LogInformation("- Location ID: {LocationId}, Located On: {LocatedOn} ({Type})", 
        trafficData.LocationId, trafficData.LocationInfo.LocatedOn, trafficData.LocationInfo.Type);
    logger.LogInformation("- Coordinates: {Latitude}, {Longitude}", trafficData.LocationInfo.Latitude, trafficData.LocationInfo.Longitude);
    logger.LogInformation("- AADT Records: {Count}", trafficData.AadtData.Count);
    logger.LogInformation("- Volume Count Records: {Count}", trafficData.VolumeCountData.Count);
    logger.LogInformation("- Volume Trend Records: {Count}", trafficData.VolumeTrendData.Count);
    
    if (trafficData.AadtData.Any())
    {
        var latestAadt = trafficData.AadtData.OrderByDescending(a => a.Year).First();
        logger.LogInformation("- Latest AADT ({Year}): {Value}", latestAadt.Year, latestAadt.Aadt);
    }
}

static async Task<int> RunTilesOnlyMode(IHost host, ILogger logger, SimpleTileGenerator tileGenerator, IConfiguration configuration)
{
    try
    {
        logger.LogInformation("🔄 Running in tiles-only mode - generating tiles from existing data");
        
        var config = configuration.GetSection("TcdsConfiguration").Get<TcdsConfiguration>();
        if (config == null)
        {
            logger.LogError("Failed to load TcdsConfiguration from appsettings.json");
            return 1;
        }

        // Try to use master consolidated file first, fall back to most recent batch file
        var masterDataFile = Path.Combine(config.DataDirectory, "parker_county_traffic_data_MASTER.json");
        string dataFileToUse;
        
        if (File.Exists(masterDataFile))
        {
            dataFileToUse = masterDataFile;
            logger.LogInformation("Using master consolidated data file: {File}", Path.GetFileName(masterDataFile));
        }
        else
        {
            logger.LogInformation("Master consolidated file not found, searching for batch files...");
            var dataFiles = Directory.GetFiles(config.DataDirectory, "parker_county_traffic_data_consolidated_*.json")
                .OrderByDescending(f => f)
                .ToList();

            if (!dataFiles.Any())
            {
                logger.LogError("No consolidated data files found in {Directory}", config.DataDirectory);
                logger.LogInformation("Available files: {Files}", string.Join(", ", Directory.GetFiles(config.DataDirectory)));
                return 1;
            }

            dataFileToUse = dataFiles.First();
            logger.LogInformation("Using most recent batch consolidated data file: {File}", Path.GetFileName(dataFileToUse));
        }

        // Read and deserialize the consolidated data
        var jsonContent = await File.ReadAllTextAsync(dataFileToUse);
        var jsonDocument = JsonSerializer.Deserialize<JsonElement>(jsonContent);
        
        var records = jsonDocument.GetProperty("records");
        var trafficData = JsonSerializer.Deserialize<List<TrafficCountData>>(records.GetRawText(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (trafficData == null || !trafficData.Any())
        {
            logger.LogError("No traffic data found in consolidated file");
            return 1;
        }

        logger.LogInformation("Loaded {Count} traffic records from consolidated data", trafficData.Count);

        // Clean up existing tiles directory to ensure fresh generation
        var tilesPath = Path.Combine(config.DataDirectory, "tiles");
        if (Directory.Exists(tilesPath))
        {
            logger.LogInformation("Cleaning up existing tiles directory");
            Directory.Delete(tilesPath, recursive: true);
        }

        // Generate fresh tiles
        await tileGenerator.GenerateTiles(trafficData, config.DataDirectory);

        logger.LogInformation("✅ Tile generation completed successfully!");
        logger.LogInformation("📁 Tiles generated in: {TilesPath}", tilesPath);
        
        // Find solution root and copy tiles to MapSandBox wwwroot
        var currentDir = Directory.GetCurrentDirectory();
        var solutionRoot = currentDir;
        while (!File.Exists(Path.Combine(solutionRoot, "MapSandBox.sln")) && Directory.GetParent(solutionRoot) != null)
        {
            solutionRoot = Directory.GetParent(solutionRoot)!.FullName;
        }
        
        var mapSandBoxTilesPath = Path.Combine(solutionRoot, "MapSandBox", "wwwroot", "tiles");
        if (Directory.Exists(Path.GetDirectoryName(mapSandBoxTilesPath)))
        {
            if (Directory.Exists(mapSandBoxTilesPath))
            {
                Directory.Delete(mapSandBoxTilesPath, recursive: true);
            }
            
            // Copy the tiles directory
            CopyDirectory(tilesPath, mapSandBoxTilesPath);
            logger.LogInformation("📁 Tiles copied to MapSandBox wwwroot: {Path}", mapSandBoxTilesPath);
        }
        
        logger.LogInformation("🚀 Ready to test in MapSandBox application!");
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while generating tiles");
        return 1;
    }
}

static async Task UpdateMasterConsolidatedDataAsync(TcdsConfiguration config, List<TrafficCountData> newTrafficData, ILogger logger, int minPage, int maxPage)
{
    try
    {
        var masterFileName = "parker_county_traffic_data_MASTER.json";
        var masterFilePath = Path.Combine(config.DataDirectory, masterFileName);
        
        List<TrafficCountData> masterData = new List<TrafficCountData>();
        var batchInfo = new List<object>();
        
        // Load existing master data if it exists
        if (File.Exists(masterFilePath))
        {
            logger.LogInformation("Loading existing master consolidated data from: {Path}", masterFilePath);
            var existingContent = await File.ReadAllTextAsync(masterFilePath);
            var existingDocument = JsonSerializer.Deserialize<JsonElement>(existingContent);
            
            if (existingDocument.TryGetProperty("records", out var recordsElement))
            {
                masterData = JsonSerializer.Deserialize<List<TrafficCountData>>(recordsElement.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) ?? new List<TrafficCountData>();
                
                logger.LogInformation("Loaded {ExistingCount} existing records from master file", masterData.Count);
            }
            
            // Load existing batch info
            if (existingDocument.TryGetProperty("batches", out var batchesElement))
            {
                batchInfo = JsonSerializer.Deserialize<List<object>>(batchesElement.GetRawText()) ?? new List<object>();
            }
        }
        else
        {
            logger.LogInformation("No existing master file found, creating new master consolidated data");
        }
        
        // Remove any existing records that match LocationIds from the new batch (to handle re-runs)
        var newLocationIds = newTrafficData.Select(r => r.LocationId).ToHashSet();
        var existingNonDuplicateRecords = masterData.Where(r => !newLocationIds.Contains(r.LocationId)).ToList();
        
        logger.LogInformation("Removing {DuplicateCount} existing duplicate records", masterData.Count - existingNonDuplicateRecords.Count);
        
        // Combine existing non-duplicate records with new records
        var updatedMasterData = existingNonDuplicateRecords.Concat(newTrafficData).ToList();
        
        // Add current batch info
        batchInfo.Add(new
        {
            MinPage = minPage,
            MaxPage = maxPage,
            RecordCount = newTrafficData.Count,
            ExtractedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
        
        // Create updated master data structure
        var masterConsolidatedData = new
        {
            ExtractedAt = DateTime.UtcNow,
            TotalRecords = updatedMasterData.Count,
            TotalBatches = batchInfo.Count,
            Batches = batchInfo,
            Records = updatedMasterData
        };
        
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var masterJsonData = JsonSerializer.Serialize(masterConsolidatedData, jsonOptions);
        await File.WriteAllTextAsync(masterFilePath, masterJsonData);
        
        logger.LogInformation("✅ Master consolidated data updated successfully!");
        logger.LogInformation("📊 Master file: {Path}", masterFilePath);
        logger.LogInformation("📈 Total records: {TotalRecords} (added {NewRecords} new, removed {RemovedDuplicates} duplicates)", 
            updatedMasterData.Count, newTrafficData.Count, masterData.Count - existingNonDuplicateRecords.Count);
        logger.LogInformation("📦 Total batches processed: {BatchCount}", batchInfo.Count);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to update master consolidated data");
        // Don't throw - this shouldn't break the main import process
    }
}

static async Task<int> RunMetaRunnerMode(IHost host, ILogger logger, SimpleTileGenerator tileGenerator, IConfiguration configuration, string[] args)
{
    try
    {
        logger.LogInformation("🚀 Starting Meta Runner Mode - Batch Orchestration System");
        
        // Parse meta runner parameters
        int batchSize = 10; // Default batch size
        int totalBatches = 5; // Default number of batches
        int waitMinutes = 5; // Default wait time between batches in minutes
        int startPage = 1; // Starting page number (kept for backward compatibility)
        int startPropNum = 1; // Starting property number (for resuming meta runs)
        bool generateTilesAfterEach = false; // Generate tiles after each batch or only at the end

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--batch-size" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedBatchSize))
            {
                batchSize = Math.Max(1, parsedBatchSize);
            }
            else if (args[i] == "--total-batches" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedTotalBatches))
            {
                totalBatches = Math.Max(1, parsedTotalBatches);
            }
            else if (args[i] == "--wait-minutes" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedWaitMinutes))
            {
                waitMinutes = Math.Max(1, parsedWaitMinutes);
            }
            else if (args[i] == "--start-page" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedStartPage))
            {
                startPage = Math.Max(1, parsedStartPage);
            }
            else if (args[i] == "--start-prop-num" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedStartPropNum))
            {
                startPropNum = Math.Max(1, parsedStartPropNum);
            }
            else if (args[i] == "--tiles-after-each")
            {
                generateTilesAfterEach = true;
            }
        }

        logger.LogInformation("📋 Meta Runner Configuration:");
        logger.LogInformation("   • Batch Size: {BatchSize} pages per batch", batchSize);
        logger.LogInformation("   • Total Batches: {TotalBatches}", totalBatches);
        logger.LogInformation("   • Starting Property: {StartPropNum}", startPropNum);
        logger.LogInformation("   • Wait Time: {WaitMinutes} minutes between batches", waitMinutes);
        logger.LogInformation("   • Generate Tiles: After {TileGeneration}", generateTilesAfterEach ? "each batch" : "all batches complete");
        
        var endPropNum = startPropNum + (totalBatches * batchSize) - 1;
        logger.LogInformation("   • Property Range: {StartProp} to {EndProp} ({TotalProps} properties)", startPropNum, endPropNum, totalBatches * batchSize);
        logger.LogInformation("   • Estimated Total Time: {TotalTime} minutes", (totalBatches - 1) * waitMinutes + (totalBatches * 5)); // Rough estimate

        var startTime = DateTime.UtcNow;
        var allBatchResults = new List<(int batchNumber, int recordsExtracted, TimeSpan duration, bool success)>();

        // Run each batch as a separate process using existing logic
        for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            var currentBatch = batchIndex + 1;
            var minPage = startPropNum + (batchIndex * batchSize);
            var maxPage = minPage + batchSize - 1;
            
            logger.LogInformation("🔄 Starting Batch {CurrentBatch}/{TotalBatches}: Pages {MinPage} to {MaxPage}", 
                currentBatch, totalBatches, minPage, maxPage);
            
            var batchStartTime = DateTime.UtcNow;
            
            try
            {
                // Execute the batch using the existing main logic
                var batchRecords = await ExecuteBatchRun(host, minPage, maxPage, generateTilesAfterEach);
                
                var batchDuration = DateTime.UtcNow - batchStartTime;
                allBatchResults.Add((currentBatch, batchRecords, batchDuration, true));
                
                logger.LogInformation("✅ Batch {CurrentBatch} completed: {Records} records in {Duration}", 
                    currentBatch, batchRecords, batchDuration.ToString(@"mm\:ss"));
                
                // Wait between batches (except after the last batch)
                if (batchIndex < totalBatches - 1)
                {
                    logger.LogInformation("⏳ Waiting {WaitMinutes} minutes before next batch... (ETA for next batch: {NextBatchTime})", 
                        waitMinutes, DateTime.Now.AddMinutes(waitMinutes).ToString("HH:mm:ss"));
                    
                    await Task.Delay(TimeSpan.FromMinutes(waitMinutes));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Batch {CurrentBatch} failed: {Error}", currentBatch, ex.Message);
                allBatchResults.Add((currentBatch, 0, DateTime.UtcNow - batchStartTime, false));
                
                logger.LogWarning("Continuing with next batch after failure...");
                if (batchIndex < totalBatches - 1)
                {
                    await Task.Delay(TimeSpan.FromMinutes(Math.Min(5, waitMinutes))); // Shorter wait after failure
                }
            }
        }

        // Generate final tiles if not generated after each batch
        if (!generateTilesAfterEach)
        {
            logger.LogInformation("🔧 Generating final consolidated tiles from all batches...");
            await RunTilesOnlyMode(host, logger, tileGenerator, configuration);
        }

        // Report final statistics
        var totalDuration = DateTime.UtcNow - startTime;
        var totalRecords = allBatchResults.Sum(r => r.recordsExtracted);
        var successfulBatches = allBatchResults.Count(r => r.success);
        
        logger.LogInformation("🎉 Meta Runner completed successfully!");
        logger.LogInformation("📊 Final Statistics:");
        logger.LogInformation("   • Total Duration: {TotalDuration}", totalDuration.ToString(@"hh\:mm\:ss"));
        logger.LogInformation("   • Successful Batches: {SuccessfulBatches}/{TotalBatches}", successfulBatches, totalBatches);
        logger.LogInformation("   • Total Records Extracted: {TotalRecords}", totalRecords);
        logger.LogInformation("   • Average Records per Batch: {AverageRecords:F1}", totalRecords / (double)Math.Max(1, successfulBatches));
        
        foreach (var (batchNumber, recordsExtracted, duration, success) in allBatchResults)
        {
            var status = success ? "✅" : "❌";
            logger.LogInformation("   • Batch {BatchNumber}: {Status} {Records} records ({Duration})", 
                batchNumber, status, recordsExtracted, duration.ToString(@"mm\:ss"));
        }

        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Meta Runner failed with error: {Error}", ex.Message);
        return 1;
    }
}

static async Task<int> ExecuteBatchRun(IHost host, int minPage, int maxPage, bool generateTiles)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var scrapingService = host.Services.GetRequiredService<TcdsScrapingService>();
    var tileGenerator = host.Services.GetRequiredService<SimpleTileGenerator>();
    var configuration = host.Services.GetRequiredService<IConfiguration>();
    
    var config = configuration.GetSection("TcdsConfiguration").Get<TcdsConfiguration>();
    if (config == null)
    {
        logger.LogError("Failed to load TcdsConfiguration from configuration");
        throw new InvalidOperationException("TcdsConfiguration not found");
    }

    try
    {
        logger.LogInformation("Starting TCDS Importer batch: pages {MinPage} to {MaxPage}", minPage, maxPage);
        
        logger.LogInformation("Navigating to TCDS website: {Url}", config.TargetUrl);
        
        var pageData = await scrapingService.NavigateToPageAsync(config.TargetUrl);
        
        logger.LogInformation("Successfully loaded page: {Title}", pageData.Title);
        logger.LogInformation("Page content length: {Length} characters", pageData.Content.Length);
        
        logger.LogInformation("Taking initial screenshot...");
        var initialScreenshotPath = await scrapingService.TakeScreenshotAsync();
        logger.LogInformation("Initial screenshot saved to: {Path}", initialScreenshotPath);
        
        logger.LogInformation("Performing Parker County search...");
        var searchResults = await scrapingService.SearchParkerCountyAsync();
        
        logger.LogInformation("Search completed. Results page title: {Title}", searchResults.Title);
        logger.LogInformation("Search metadata: {Metadata}", string.Join(", ", searchResults.Metadata.Select(kv => $"{kv.Key}={kv.Value}")));
        
        logger.LogInformation("Taking final screenshot after search...");
        var finalScreenshotPath = await scrapingService.TakeScreenshotAsync();
        logger.LogInformation("Final screenshot saved to: {Path}", finalScreenshotPath);
        
        var allTrafficData = new List<TrafficCountData>();
        
        // Navigate to the starting page using Goto Record if not page 1
        if (minPage > 1)
        {
            logger.LogInformation("Navigating directly to starting record {MinPage} using Goto Record...", minPage);
            try
            {
                await scrapingService.GotoRecordAsync(minPage);
                logger.LogInformation("Successfully navigated to starting record {MinPage}", minPage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to navigate to starting record {MinPage} using Goto Record", minPage);
                throw;
            }
        }
        
        logger.LogInformation("Starting data extraction loop for pages {MinPage} to {MaxPage} (total: {PageCount} pages)", 
            minPage, maxPage, maxPage - minPage + 1);
        
        for (int pageNumber = minPage; pageNumber <= maxPage; pageNumber++)
        {
            logger.LogInformation("Processing page {PageNumber} of {MaxPage}...", pageNumber, maxPage);
            
            // Extract data from current page
            logger.LogInformation("Extracting structured traffic count data from page {PageNumber}...", pageNumber);
            var trafficData = await scrapingService.ExtractTrafficDataAsync();
            
            // Save individual page data
            await SaveTrafficDataAsync(config, trafficData, $"page_{pageNumber}", logger);
            allTrafficData.Add(trafficData);
            
            logger.LogInformation("Page {PageNumber} data summary:", pageNumber);
            LogTrafficDataSummary(trafficData, logger);
            
            // Take screenshot of current page
            logger.LogInformation("Taking screenshot of page {PageNumber}...", pageNumber);
            var screenshotPath = await scrapingService.TakeScreenshotAsync();
            logger.LogInformation("Page {PageNumber} screenshot saved to: {Path}", pageNumber, screenshotPath);
            
            // Try to navigate to next page (except on the last allowed page)
            if (pageNumber < maxPage)
            {
                logger.LogInformation("Attempting to navigate to page {NextPage}...", pageNumber + 1);
                
                try
                {
                    var nextPageData = await scrapingService.ClickNextRecordAsync();
                    logger.LogInformation("Successfully navigated to page {NextPage}: {Title}", pageNumber + 1, nextPageData.Title);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not navigate to page {NextPage}. Reached end of available records at page {CurrentPage}", pageNumber + 1, pageNumber);
                    break;
                }
            }
        }
        
        logger.LogInformation("Data extraction completed. Processed {PageCount} pages with {TotalRecords} total records", 
            allTrafficData.Count, allTrafficData.Count);
        
        // Save consolidated data
        await SaveConsolidatedDataAsync(config, allTrafficData, logger, minPage, maxPage);
        
        // Export as GeoJSON for mapping applications
        await ExportGeoJsonAsync(config, allTrafficData, logger, minPage, maxPage);
        
        // Generate spatial tiles for progressive loading if requested
        if (generateTiles)
        {
            await ExportTiledGeoJsonAsync(config, allTrafficData, tileGenerator, logger);
        }
        
        logger.LogInformation("TCDS Importer batch completed successfully - {PageCount} pages ({MinPage} to {MaxPage}) of Parker County data extracted and saved", allTrafficData.Count, minPage, maxPage);
        
        return allTrafficData.Count;
    }
    finally
    {
        await scrapingService.DisposeAsync();
    }
}

static async Task<int> RunEnhancedMergeMode(IHost host, ILogger logger, string solutionRoot)
{
    try
    {
        Console.WriteLine("🚦 Enhanced Traffic Data Matcher - Testing I-20 Fix");
        Console.WriteLine("=" + new string('=', 59));

        var enhancedMerger = host.Services.GetRequiredService<EnhancedRoadTrafficMerger>();
        var qualityMonitor = host.Services.GetRequiredService<DataQualityMonitor>();

        // Test with existing data to see current I-20 issue
        await TestCurrentDataQuality(logger, solutionRoot);

        // Test the enhanced merger
        await TestEnhancedMerger(enhancedMerger, qualityMonitor, logger, solutionRoot);

        Console.WriteLine("\n✅ Enhanced testing complete! Check logs for detailed results.");
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during enhanced testing");
        Console.WriteLine($"❌ Error: {ex.Message}");
        return 1;
    }
}

static async Task TestCurrentDataQuality(ILogger logger, string solutionRoot)
{
    logger.LogInformation("🔍 Testing current data quality (before fix)");

    // Check if the current problematic files exist
    var currentTrafficFile = Path.Combine(solutionRoot, "MapSandBox", "wwwroot", "parker-roads-with-traffic.geojson");

    if (File.Exists(currentTrafficFile))
    {
        logger.LogInformation("📁 Found current traffic data file: {File}", Path.GetFileName(currentTrafficFile));

        // Read and analyze current I-20 data
        var content = await File.ReadAllTextAsync(currentTrafficFile);
        var i20Matches = System.Text.RegularExpressions.Regex.Matches(content, @"""FULLNAME"":\s*""I-\s*20"".*?""aadt"":\s*(\d+)");

        if (i20Matches.Count > 0)
        {
            logger.LogWarning("🛣️  Current I-20 AADT values found:");
            foreach (System.Text.RegularExpressions.Match match in i20Matches.Take(3))
            {
                var aadt = int.Parse(match.Groups[1].Value);
                logger.LogWarning("   • I-20 segment AADT: {Aadt:N0}", aadt);

                if (aadt == 8388)
                {
                    logger.LogError("   🚨 CONFIRMED: I-20 has problematic ramp AADT value: {Aadt}", aadt);
                }
            }
        }
    }
    else
    {
        logger.LogWarning("📁 Current traffic file not found: {File}", Path.GetFileName(currentTrafficFile));
    }
}

static async Task TestEnhancedMerger(EnhancedRoadTrafficMerger merger, DataQualityMonitor monitor, ILogger logger, string solutionRoot)
{
    logger.LogInformation("🚀 Testing enhanced traffic data merger");

    var roadGeoJsonPath = Path.Combine(solutionRoot, "MapSandBox", "wwwroot", "parker-county-roads.geojson");
    var masterDataPath = Path.Combine(solutionRoot, "TCDS.Importer", "Data", "parker_county_traffic_data_MASTER.json");
    var outputDirectory = Path.Combine(solutionRoot, "TestOutput");

    // Check if required files exist
    if (!File.Exists(roadGeoJsonPath))
    {
        logger.LogError("❌ Road data file not found: {File}", roadGeoJsonPath);
        return;
    }

    if (!File.Exists(masterDataPath))
    {
        logger.LogWarning("⚠️  Master data file not found: {File}", masterDataPath);
        logger.LogInformation("💡 Will try alternative traffic data sources...");

        // Try the GeoJSON traffic file instead
        var alternativeTrafficPath = Path.Combine(solutionRoot, "MapSandBox", "wwwroot", "parker_county_traffic_locations_20250731_111008.geojson");
        if (File.Exists(alternativeTrafficPath))
        {
            logger.LogInformation("📁 Using alternative traffic data: {File}", Path.GetFileName(alternativeTrafficPath));
            await TestWithAlternativeData(roadGeoJsonPath, alternativeTrafficPath, outputDirectory, logger);
            return;
        }
        else
        {
            logger.LogError("❌ No suitable traffic data files found");
            return;
        }
    }

    try
    {
        // Run the enhanced merger
        var outputPath = await merger.MergeRoadTrafficDataFromMasterAsync(
            roadGeoJsonPath,
            masterDataPath,
            outputDirectory,
            excludeInactiveStations: true
        );

        logger.LogInformation("✅ Enhanced merger completed. Output: {OutputPath}", outputPath);

        // Verify I-20 fix
        await VerifyI20Fix(outputPath, logger);
    }
    catch (FileNotFoundException ex)
    {
        logger.LogError("📁 File not found: {Message}", ex.Message);
        logger.LogInformation("💡 Trying with available data sources...");
    }
}

static async Task TestWithAlternativeData(string roadGeoJsonPath, string trafficGeoJsonPath, string outputDirectory, ILogger logger)
{
    logger.LogInformation("🔄 Testing with GeoJSON traffic data");

    // For now, just analyze the traffic data structure
    var trafficJson = await File.ReadAllTextAsync(trafficGeoJsonPath);

    // Look for the problematic location 184CC1
    if (trafficJson.Contains("184CC1"))
    {
        logger.LogWarning("🔍 Found problematic location 184CC1 in traffic data");

        // Extract AADT for this location
        var match = System.Text.RegularExpressions.Regex.Match(
            trafficJson,
            @"""locationId"":\s*""184CC1"".*?""latestAadt"":\s*(\d+).*?""category"":\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (match.Success)
        {
            var aadt = int.Parse(match.Groups[1].Value);
            var category = match.Groups[2].Value;

            logger.LogWarning("📊 Location 184CC1 Analysis:");
            logger.LogWarning("   • AADT: {Aadt:N0}", aadt);
            logger.LogWarning("   • Category: {Category}", category);

            if (category.Equals("RAMP", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("🚨 CONFIRMED: Location 184CC1 is a RAMP with AADT {Aadt}", aadt);
                logger.LogError("🚨 This ramp data should NOT be applied to I-20 mainline!");
            }
        }
    }

    // Count different traffic location types
    var rampMatches = System.Text.RegularExpressions.Regex.Matches(trafficJson, @"""category"":\s*""RAMP""");
    var mainlineMatches = System.Text.RegularExpressions.Regex.Matches(trafficJson, @"""category"":\s*""MAINLINE""");

    logger.LogInformation("📊 Traffic Data Inventory:");
    logger.LogInformation("   • RAMP locations: {Count}", rampMatches.Count);
    logger.LogInformation("   • MAINLINE locations: {Count}", mainlineMatches.Count);
}

static async Task VerifyI20Fix(string outputPath, ILogger logger)
{
    logger.LogInformation("🔍 Verifying I-20 fix in output data");

    if (!File.Exists(outputPath))
    {
        logger.LogError("❌ Output file not found: {File}", outputPath);
        return;
    }

    var content = await File.ReadAllTextAsync(outputPath);
    var i20Matches = System.Text.RegularExpressions.Regex.Matches(
        content,
        @"""fullName"":\s*""I-\s*20"".*?""traffic"":\s*{[^}]*""aadt"":\s*(\d+)[^}]*}.*?""trafficMatch"":\s*{[^}]*""sourceType"":\s*""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    if (i20Matches.Count > 0)
    {
        logger.LogInformation("🛣️  I-20 Fix Verification Results:");

        bool fixSuccessful = true;
        foreach (System.Text.RegularExpressions.Match match in i20Matches.Take(5))
        {
            var aadt = int.Parse(match.Groups[1].Value);
            var sourceType = match.Groups[2].Value;

            logger.LogInformation("   • I-20 segment AADT: {Aadt:N0}, Source: {SourceType}", aadt, sourceType);

            if (aadt == 8388 || sourceType.Contains("Ramp"))
            {
                logger.LogError("   🚨 ISSUE: I-20 still using ramp data (AADT: {Aadt}, Source: {SourceType})", aadt, sourceType);
                fixSuccessful = false;
            }
            else if (aadt >= 25000)
            {
                logger.LogInformation("   ✅ GOOD: I-20 AADT {Aadt:N0} is within expected range", aadt);
            }
            else
            {
                logger.LogWarning("   ⚠️  WARNING: I-20 AADT {Aadt:N0} is lower than expected (25,000+)", aadt);
            }
        }

        if (fixSuccessful)
        {
            logger.LogInformation("✅ I-20 FIX SUCCESSFUL: No more ramp contamination detected");
        }
        else
        {
            logger.LogError("❌ I-20 FIX INCOMPLETE: Issues still detected");
        }
    }
    else
    {
        logger.LogWarning("⚠️  No I-20 segments found in output data");
    }
}

static async Task<int> RunRoadTrafficMergeMode(IHost host, ILogger logger, string solutionRoot)
{
    try
    {
        logger.LogInformation("🛣️ Starting Road-Traffic Merge Mode");
        
        var roadTrafficMerger = host.Services.GetRequiredService<RoadTrafficMerger>();
        
        // Define input and output paths - use MASTER data instead of limited GeoJSON
        var roadGeoJsonPath = Path.Combine(solutionRoot, "MapSandBox", "wwwroot", "parker-county-roads.geojson");
        var masterDataPath = Path.Combine(solutionRoot, "TCDS.Importer", "Data", "parker_county_traffic_data_MASTER.json");
        var outputDirectory = Path.Combine(solutionRoot, "MapSandBox", "wwwroot");
        
        logger.LogInformation("📍 Input paths:");
        logger.LogInformation("   • Roads: {RoadPath}", roadGeoJsonPath);
        logger.LogInformation("   • Traffic MASTER data: {MasterPath}", masterDataPath);
        logger.LogInformation("   • Output: {OutputDir}", outputDirectory);
        
        // Verify input files exist
        if (!File.Exists(roadGeoJsonPath))
        {
            logger.LogError("❌ Roads file not found: {RoadPath}", roadGeoJsonPath);
            return 1;
        }
        
        if (!File.Exists(masterDataPath))
        {
            logger.LogError("❌ MASTER traffic data file not found: {MasterPath}", masterDataPath);
            return 1;
        }
        
        // Perform the merge using MASTER data
        var outputPath = await roadTrafficMerger.MergeRoadTrafficDataFromMasterAsync(
            roadGeoJsonPath, 
            masterDataPath, 
            outputDirectory);
        
        logger.LogInformation("🎉 Road-Traffic merge completed successfully!");
        logger.LogInformation("📁 Enhanced dataset created: {OutputPath}", outputPath);
        
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Road-Traffic merge failed: {Error}", ex.Message);
        return 1;
    }
}

static void CopyDirectory(string sourceDir, string destinationDir)
{
    var dir = new DirectoryInfo(sourceDir);
    DirectoryInfo[] dirs = dir.GetDirectories();

    Directory.CreateDirectory(destinationDir);

    foreach (FileInfo file in dir.GetFiles())
    {
        string targetFilePath = Path.Combine(destinationDir, file.Name);
        file.CopyTo(targetFilePath);
    }

    foreach (DirectoryInfo subDir in dirs)
    {
        string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
        CopyDirectory(subDir.FullName, newDestinationDir);
    }
}
