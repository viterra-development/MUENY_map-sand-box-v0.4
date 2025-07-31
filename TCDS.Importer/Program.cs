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

builder.Logging.AddConsole();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
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
    
    const int maxPages = 10;
    var allTrafficData = new List<TrafficCountData>();
    
    logger.LogInformation("Starting data extraction loop for up to {MaxPages} pages", maxPages);
    
    for (int pageNumber = 1; pageNumber <= maxPages; pageNumber++)
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
    await SaveConsolidatedDataAsync(config, allTrafficData, logger);
    
    // Export as GeoJSON for mapping applications
    await ExportGeoJsonAsync(config, allTrafficData, logger);
    
    logger.LogInformation("TCDS Importer completed successfully - {PageCount} pages of Parker County data extracted and saved", allTrafficData.Count);
    
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

static async Task SaveConsolidatedDataAsync(TcdsConfiguration config, List<TrafficCountData> allTrafficData, ILogger logger)
{
    // Create data directory if it doesn't exist
    Directory.CreateDirectory(config.DataDirectory);
    
    // Save consolidated data as JSON
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var jsonFileName = $"parker_county_traffic_data_consolidated_{timestamp}.json";
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
        Records = allTrafficData
    };
    
    var jsonData = JsonSerializer.Serialize(consolidatedData, jsonOptions);
    await File.WriteAllTextAsync(jsonFilePath, jsonData);
    
    logger.LogInformation("Consolidated traffic data saved to: {Path}", jsonFilePath);
}

static async Task ExportGeoJsonAsync(TcdsConfiguration config, List<TrafficCountData> allTrafficData, ILogger logger)
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
                exportedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                source = "Texas Department of Transportation TCDS",
                county = "Parker County, Texas"
            },
            features = features
        };
        
        // Save GeoJSON file
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var geoJsonFileName = $"parker_county_traffic_locations_{timestamp}.geojson";
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
