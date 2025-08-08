using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using System.Text.Json;

namespace TCDS.Importer.Services;

public class SimpleTileGenerator
{
    private readonly ILogger<SimpleTileGenerator> _logger;

    public SimpleTileGenerator(ILogger<SimpleTileGenerator> logger)
    {
        _logger = logger;
    }

    public async Task GenerateTiles(List<TrafficCountData> trafficData, string outputPath, bool excludeInactiveStations = true)
    {
        _logger.LogInformation("Generating tiles for {RecordCount} traffic records (excludeInactive: {ExcludeInactive})", trafficData.Count, excludeInactiveStations);

        // Filter records with valid coordinates and optionally exclude inactive stations
        var validRecords = trafficData
            .Where(r => r.LocationInfo.Latitude.HasValue && r.LocationInfo.Longitude.HasValue)
            .Where(r => !excludeInactiveStations || r.LocationInfo.Active != "No")
            .ToList();

        _logger.LogInformation("Found {ValidCount} records with valid coordinates", validRecords.Count);

        // Generate tiles for different zoom levels (from wide view down to street level)
        await GenerateZoomLevel(validRecords, outputPath, 2, "continent-level");
        await GenerateZoomLevel(validRecords, outputPath, 4, "country-level");
        await GenerateZoomLevel(validRecords, outputPath, 6, "state-level");
        await GenerateZoomLevel(validRecords, outputPath, 8, "regional-level");
        await GenerateZoomLevel(validRecords, outputPath, 10, "county-level");
        
        // Generate complete coverage for traffic count visibility range (12-16)
        for (int zoom = 12; zoom <= 16; zoom++)
        {
            await GenerateZoomLevel(validRecords, outputPath, zoom, GetZoomDescription(zoom));
        }

        _logger.LogInformation("Tile generation completed successfully");
    }

    private string GetZoomDescription(int zoom) => zoom switch
    {
        12 => "city-overview",
        13 => "city-detail", 
        14 => "neighborhood",
        15 => "neighborhood-detail",
        16 => "street-level",
        _ => $"zoom-{zoom}"
    };

    private async Task GenerateZoomLevel(List<TrafficCountData> records, string basePath, int zoom, string description)
    {
        _logger.LogInformation("Generating zoom level {Zoom} ({Description})", zoom, description);

        // Create zoom directory
        var zoomPath = Path.Combine(basePath, "tiles", "traffic-counts", zoom.ToString());
        Directory.CreateDirectory(zoomPath);

        // For Parker County, we'll use a simple spatial bucketing approach
        var parkerCountyBounds = new
        {
            MinLat = 32.6,   // South boundary
            MaxLat = 32.9,   // North boundary  
            MinLng = -98.1,  // West boundary
            MaxLng = -97.5   // East boundary
        };

        // Calculate tiles needed for Parker County at this zoom level
        var tiles = GetTilesForBounds(parkerCountyBounds.MinLat, parkerCountyBounds.MinLng, 
                                     parkerCountyBounds.MaxLat, parkerCountyBounds.MaxLng, zoom);

        foreach (var tile in tiles)
        {
            var tileRecords = GetRecordsInTile(records, tile, zoom);
            
            // Only generate tiles that contain data (sparse data optimization)
            if (tileRecords.Any())
            {
                await GenerateTileFile(tileRecords, zoomPath, tile.X, tile.Y);
                _logger.LogDebug("Generated tile {Zoom}/{X}/{Y} with {Count} records", zoom, tile.X, tile.Y, tileRecords.Count);
            }
        }

        _logger.LogInformation("Completed zoom level {Zoom} with {TileCount} tiles", zoom, tiles.Count());
    }

    private IEnumerable<(int X, int Y)> GetTilesForBounds(double minLat, double minLng, double maxLat, double maxLng, int zoom)
    {
        var minTileX = LngToTileX(minLng, zoom);
        var maxTileX = LngToTileX(maxLng, zoom);
        var minTileY = LatToTileY(maxLat, zoom); // Note: Y is flipped in tile coordinates
        var maxTileY = LatToTileY(minLat, zoom);

        var tiles = new List<(int X, int Y)>();
        for (int x = minTileX; x <= maxTileX; x++)
        {
            for (int y = minTileY; y <= maxTileY; y++)
            {
                tiles.Add((x, y));
            }
        }

        return tiles;
    }

    private List<TrafficCountData> GetRecordsInTile(List<TrafficCountData> records, (int X, int Y) tile, int zoom)
    {
        var tileBounds = GetTileBounds(tile.X, tile.Y, zoom);
        
        return records.Where(record =>
        {
            var lat = (double)record.LocationInfo.Latitude!.Value;
            var lng = (double)record.LocationInfo.Longitude!.Value;
            
            return lat >= tileBounds.MinLat && lat <= tileBounds.MaxLat &&
                   lng >= tileBounds.MinLng && lng <= tileBounds.MaxLng;
        }).ToList();
    }

    private (double MinLat, double MaxLat, double MinLng, double MaxLng) GetTileBounds(int x, int y, int zoom)
    {
        var minLng = TileXToLng(x, zoom);
        var maxLng = TileXToLng(x + 1, zoom);
        var minLat = TileYToLat(y + 1, zoom);
        var maxLat = TileYToLat(y, zoom);

        return (minLat, maxLat, minLng, maxLng);
    }

    private async Task GenerateTileFile(List<TrafficCountData> records, string zoomPath, int x, int y)
    {
        // Create tile directory structure
        var xPath = Path.Combine(zoomPath, x.ToString());
        Directory.CreateDirectory(xPath);

        // Convert to GeoJSON features
        var features = records.Select(record =>
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
                    
                    // Processing metadata
                    extractedAt = record.ExtractedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            };
        }).ToList();

        var geoJson = new
        {
            type = "FeatureCollection",
            features = features
        };

        // Save tile file
        var tileFilePath = Path.Combine(xPath, $"{y}.geojson");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false, // Compact for tiles
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var jsonData = JsonSerializer.Serialize(geoJson, jsonOptions);
        await File.WriteAllTextAsync(tileFilePath, jsonData);
    }

    // Tile coordinate conversion utilities
    private int LngToTileX(double lng, int zoom)
    {
        return (int)Math.Floor((lng + 180.0) / 360.0 * Math.Pow(2.0, zoom));
    }

    private int LatToTileY(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0 * Math.Pow(2.0, zoom));
    }

    private double TileXToLng(int x, int zoom)
    {
        return x / Math.Pow(2.0, zoom) * 360.0 - 180.0;
    }

    private double TileYToLat(int y, int zoom)
    {
        var n = Math.PI - 2.0 * Math.PI * y / Math.Pow(2.0, zoom);
        return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
    }
}