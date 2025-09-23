using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NoaaDataProcessor.Models;
using System.Text.Json;

namespace NoaaDataProcessor.Services;

public class SpatialClippingService
{
    private readonly ILogger<SpatialClippingService> _logger;
    private readonly GeometryFactory _geometryFactory;

    public SpatialClippingService(ILogger<SpatialClippingService> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory();
    }

    public async Task<List<RainfallPoint>> ClipToCountyBoundaryAsync(List<GridCell> gridCells, string boundaryFilePath)
    {
        _logger.LogInformation("Loading county boundary from: {BoundaryFile}", boundaryFilePath);

        if (!File.Exists(boundaryFilePath))
        {
            throw new FileNotFoundException($"County boundary file not found: {boundaryFilePath}");
        }

        // Load the county boundary
        var boundaryGeojson = await File.ReadAllTextAsync(boundaryFilePath);
        var geoJsonReader = new GeoJsonReader();
        var featureCollection = geoJsonReader.Read<FeatureCollection>(boundaryGeojson);

        if (featureCollection == null || featureCollection.Count == 0)
        {
            throw new InvalidOperationException("No features found in county boundary file");
        }

        // Get the first polygon feature (Parker County boundary)
        var countyFeature = featureCollection[0];
        var countyGeometry = countyFeature.Geometry;

        if (countyGeometry == null)
        {
            throw new InvalidOperationException("County boundary feature has no geometry");
        }

        _logger.LogInformation("County boundary loaded. Geometry type: {GeometryType}", countyGeometry.GeometryType);

        // Convert grid cells to points and filter by county boundary
        var rainfallPoints = new List<RainfallPoint>();
        var totalCells = gridCells.Count;
        var processedCells = 0;

        foreach (var cell in gridCells)
        {
            processedCells++;
            if (processedCells % 10000 == 0)
            {
                _logger.LogInformation("Processed {Processed}/{Total} cells ({Percentage:F1}%)",
                    processedCells, totalCells, (double)processedCells / totalCells * 100);
            }

            var point = _geometryFactory.CreatePoint(new Coordinate(cell.Longitude, cell.Latitude));

            if (countyGeometry.Contains(point))
            {
                rainfallPoints.Add(new RainfallPoint
                {
                    Longitude = Math.Round(cell.Longitude, 6),
                    Latitude = Math.Round(cell.Latitude, 6),
                    RainfallValue = cell.Value
                });
            }
        }

        _logger.LogInformation("Spatial clipping complete. {ClippedPoints} points within county boundary from {TotalCells} total cells",
            rainfallPoints.Count, totalCells);

        return rainfallPoints;
    }
}