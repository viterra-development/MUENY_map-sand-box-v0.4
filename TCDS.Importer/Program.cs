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
    
    logger.LogInformation("Extracting structured traffic count data from first page...");
    var firstPageTrafficData = await scrapingService.ExtractTrafficDataAsync();
    
    // Save first page data
    await SaveTrafficDataAsync(config, firstPageTrafficData, "page_1", logger);
    
    logger.LogInformation("First page data summary:");
    LogTrafficDataSummary(firstPageTrafficData, logger);
    
    logger.LogInformation("Navigating to second page...");
    var secondPageData = await scrapingService.ClickNextRecordAsync();
    
    logger.LogInformation("Successfully navigated to second page: {Title}", secondPageData.Title);
    
    logger.LogInformation("Taking screenshot of second page...");
    var secondPageScreenshotPath = await scrapingService.TakeScreenshotAsync();
    logger.LogInformation("Second page screenshot saved to: {Path}", secondPageScreenshotPath);
    
    logger.LogInformation("Extracting structured traffic count data from second page...");
    var secondPageTrafficData = await scrapingService.ExtractTrafficDataAsync();
    
    // Save second page data
    await SaveTrafficDataAsync(config, secondPageTrafficData, "page_2", logger);
    
    logger.LogInformation("Second page data summary:");
    LogTrafficDataSummary(secondPageTrafficData, logger);
    
    logger.LogInformation("TCDS Importer completed successfully - Two pages of Parker County data extracted and saved");
    
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

static void LogTrafficDataSummary(TrafficCountData trafficData, ILogger logger)
{
    logger.LogInformation("- Location: {Location} ({Type})", trafficData.LocationInfo.LocatedOn, trafficData.LocationInfo.Type);
    logger.LogInformation("- AADT Records: {Count}", trafficData.AadtData.Count);
    logger.LogInformation("- Volume Count Records: {Count}", trafficData.VolumeCountData.Count);
    logger.LogInformation("- Volume Trend Records: {Count}", trafficData.VolumeTrendData.Count);
    
    if (trafficData.AadtData.Any())
    {
        var latestAadt = trafficData.AadtData.OrderByDescending(a => a.Year).First();
        logger.LogInformation("- Latest AADT ({Year}): {Value}", latestAadt.Year, latestAadt.Aadt);
    }
}
