using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NoaaDataProcessor.Models;
using System.Text.Json;

namespace NoaaDataProcessor.Services;

public class GeoJsonExporter
{
    private readonly ILogger<GeoJsonExporter> _logger;
    private readonly GeometryFactory _geometryFactory;

    public GeoJsonExporter(ILogger<GeoJsonExporter> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory();
    }

    public async Task ExportAsync(List<RainfallPoint> rainfallPoints, string outputPath)
    {
        _logger.LogInformation("Exporting {PointCount} rainfall points to: {OutputPath}",
            rainfallPoints.Count, outputPath);

        var features = new List<Feature>();

        foreach (var point in rainfallPoints)
        {
            var geometry = _geometryFactory.CreatePoint(new Coordinate(point.Longitude, point.Latitude));
            var attributes = new AttributesTable
            {
                { "rainfall", point.RainfallValue },
                { "rainfallInches", point.RainfallInches }
            };

            features.Add(new Feature(geometry, attributes));
        }

        // Serialize to a JSON array of GeoJSON Feature objects (not a FeatureCollection)
        // This matches deck.gl Scatterplot/Heatmap expectations without needing a dataTransform.
        var geoJsonWriter = new GeoJsonWriter();
        var featureJsonStrings = features.Select(f => geoJsonWriter.Write(f));
        var geoJsonString = "[" + string.Join(",", featureJsonStrings) + "]";

        // Ensure output directory exists
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            _logger.LogInformation("Created output directory: {Directory}", outputDirectory);
        }

        // Write to file
        await File.WriteAllTextAsync(outputPath, geoJsonString);

        // Log file size
        var fileInfo = new FileInfo(outputPath);
        _logger.LogInformation("GeoJSON export complete. File size: {FileSize} bytes ({FileSizeKB:F1} KB)",
            fileInfo.Length, fileInfo.Length / 1024.0);

        // Validate the exported file
        await ValidateExportedFileAsync(outputPath);
    }

    private async Task ValidateExportedFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var jsonDocument = JsonDocument.Parse(content);

            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
            {
                var featureCount = jsonDocument.RootElement.GetArrayLength();
                _logger.LogInformation("Validation successful. GeoJSON Feature array contains {FeatureCount} features", featureCount);
            }
            else if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object &&
                     jsonDocument.RootElement.TryGetProperty("type", out var typeProperty) &&
                     typeProperty.GetString() == "FeatureCollection" &&
                     jsonDocument.RootElement.TryGetProperty("features", out var featuresProperty))
            {
                var featureCount = featuresProperty.GetArrayLength();
                _logger.LogInformation("Validation successful. GeoJSON FeatureCollection contains {FeatureCount} features", featureCount);
            }
            else
            {
                _logger.LogWarning("Validation warning: Exported file format is not an array or FeatureCollection");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed: Unable to parse exported GeoJSON file");
            throw;
        }
    }
}