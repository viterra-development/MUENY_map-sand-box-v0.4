using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoaaDataProcessor.Services;

namespace NoaaDataProcessor;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("=== NOAA Rainfall Data Processor Started ===");

            // Get configuration values
            var inputRainfallGrid = configuration["InputPaths:RainfallGrid"];
            var inputCountyBoundary = configuration["InputPaths:CountyBoundary"];
            var outputGeoJson = configuration["OutputPaths:RainfallGeoJson"];

            if (string.IsNullOrEmpty(inputRainfallGrid) || string.IsNullOrEmpty(inputCountyBoundary) || string.IsNullOrEmpty(outputGeoJson))
            {
                throw new InvalidOperationException("Required configuration paths are missing");
            }

            logger.LogInformation("Input rainfall grid: {InputGrid}", inputRainfallGrid);
            logger.LogInformation("Input county boundary: {InputBoundary}", inputCountyBoundary);
            logger.LogInformation("Output GeoJSON: {Output}", outputGeoJson);

            // Get services
            var gridParser = serviceProvider.GetRequiredService<AsciiGridParser>();
            var spatialClipper = serviceProvider.GetRequiredService<SpatialClippingService>();
            var geoJsonExporter = serviceProvider.GetRequiredService<GeoJsonExporter>();

            // Step 1: Parse ASCII grid
            logger.LogInformation("Step 1: Parsing ASCII grid file...");
            var (header, gridCells) = await gridParser.ParseAsync(inputRainfallGrid);

            logger.LogInformation("Grid parsed successfully:");
            logger.LogInformation("  - Dimensions: {NCols} x {NRows}", header.NCols, header.NRows);
            logger.LogInformation("  - Extent: ({XllCorner}, {YllCorner}) to ({XurCorner}, {YurCorner})",
                header.XllCorner, header.YllCorner, header.XurCorner, header.YurCorner);
            logger.LogInformation("  - Cell size: {CellSize}°", header.CellSize);
            logger.LogInformation("  - Valid cells: {ValidCells}", gridCells.Count);

            // Step 2: Clip to Parker County boundary
            logger.LogInformation("Step 2: Clipping to Parker County boundary...");
            var rainfallPoints = await spatialClipper.ClipToCountyBoundaryAsync(gridCells, inputCountyBoundary);

            if (!rainfallPoints.Any())
            {
                logger.LogWarning("No rainfall points found within Parker County boundary. Check coordinate systems and data alignment.");
                return;
            }

            logger.LogInformation("Clipping completed. {PointCount} points within Parker County", rainfallPoints.Count);

            // Log rainfall statistics
            var minRainfall = rainfallPoints.Min(p => p.RainfallInches);
            var maxRainfall = rainfallPoints.Max(p => p.RainfallInches);
            var avgRainfall = rainfallPoints.Average(p => p.RainfallInches);

            logger.LogInformation("Rainfall statistics:");
            logger.LogInformation("  - Min: {MinRainfall:F2} inches", minRainfall);
            logger.LogInformation("  - Max: {MaxRainfall:F2} inches", maxRainfall);
            logger.LogInformation("  - Average: {AvgRainfall:F2} inches", avgRainfall);

            // Step 3: Export to GeoJSON
            logger.LogInformation("Step 3: Exporting to GeoJSON...");
            await geoJsonExporter.ExportAsync(rainfallPoints, outputGeoJson);

            logger.LogInformation("=== NOAA Rainfall Data Processor Completed Successfully ===");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error occurred during processing");
            Environment.Exit(1);
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        // Add configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Add application services
        services.AddScoped<AsciiGridParser>();
        services.AddScoped<SpatialClippingService>();
        services.AddScoped<GeoJsonExporter>();
    }
}