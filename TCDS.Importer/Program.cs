using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using TCDS.Importer.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

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
    logger.LogInformation("TCDS Importer completed successfully - Parker County search performed");
    
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
