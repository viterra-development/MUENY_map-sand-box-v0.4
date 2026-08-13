using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Writes trip generation results to GeoJSON output files.
/// Produces enriched parcel GeoJSON and aggregated road GeoJSON.
/// </summary>
public class OutputWriter
{
    private readonly ILogger<OutputWriter> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OutputWriter(ILogger<OutputWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Write parcels with trip data to GeoJSON.
    /// </summary>
    public void WriteParcels(
        string outputPath,
        List<CadParcel> parcels,
        List<TripGenerationResult> results)
    {
        _logger.LogInformation("Writing parcel GeoJSON to {Path}", outputPath);
        EnsureDirectory(outputPath);

        // First-write-wins guard against duplicate parcel IDs (TxGIO includes ~6.5K
        // rows with prop_id="0" for rights-of-way + minor dupes).
        var resultMap = new Dictionary<string, TripGenerationResult>(results.Count);
        foreach (var r in results)
        {
            if (!resultMap.ContainsKey(r.ParcelId)) resultMap[r.ParcelId] = r;
        }
        var writer = new GeoJsonWriter();
        var fc = new FeatureCollection();

        foreach (var parcel in parcels)
        {
            if (parcel.Geometry == null) continue;

            var attrs = new AttributesTable
            {
                { "parcel_id", parcel.ParcelId },
                { "state_cd", parcel.StateCd },
                // Owner name is deliberately NOT written to output: these files are
                // served publicly and CAD owner data must stay out of them
                // (Texas Tax Code sec. 25.025 protected-person addresses).
                // The map only needs city-ownership as a boolean for styling.
                { "city_owned", (parcel.OwnerName ?? "").ToUpperInvariant().Contains("CITY OF") },
                { "legal_desc", parcel.LegalDesc ?? "" },
                { "situs_street", parcel.SitusStreet ?? "" },
                { "legal_acreage", parcel.LegalAcreage },
                { "imprv_sqft", parcel.ImprvSqft },
                { "imprv_val", parcel.ImprvVal },
                { "total_val", parcel.TotalVal },
            };

            if (resultMap.TryGetValue(parcel.ParcelId, out var result))
            {
                attrs.Add("ite_code", result.IteCode);
                attrs.Add("ite_land_use", result.IteLandUse);
                attrs.Add("units", result.Units);
                attrs.Add("daily_trips", result.DailyTrips);
                attrs.Add("am_peak_trips", result.AmPeakTrips);
                attrs.Add("pm_peak_trips", result.PmPeakTrips);
                attrs.Add("classification", result.Classification);
                attrs.Add("access_road", result.AccessRoadName ?? "");
                attrs.Add("access_segment_id", result.AccessSegmentId ?? "");
                attrs.Add("snap_distance_m", result.SnapDistanceMeters);
                attrs.Add("notes", result.Notes ?? "");
            }

            fc.Add(new Feature(parcel.Geometry, attrs));
        }

        var json = writer.Write(fc);
        File.WriteAllText(outputPath, json);
        _logger.LogInformation("Wrote {Count} parcel features to {Path}", fc.Count, outputPath);
    }

    /// <summary>
    /// Write road segments with aggregated trip data to GeoJSON.
    /// </summary>
    public void WriteRoads(string outputPath, List<RoadSegment> roads)
    {
        _logger.LogInformation("Writing road GeoJSON to {Path}", outputPath);
        EnsureDirectory(outputPath);

        var writer = new GeoJsonWriter();
        var fc = new FeatureCollection();

        foreach (var road in roads)
        {
            if (road.Geometry == null) continue;

            var attrs = new AttributesTable
            {
                { "linear_id", road.LinearId },
                { "full_name", road.FullName },
                { "mtfcc", road.Mtfcc ?? "" },
                { "aadt", road.Aadt },
                { "aadt_year", road.AadtYear },
                { "functional_class", road.FunctionalClass ?? "" },
                { "trip_daily_total", Math.Round(road.TotalDailyTrips, 0) },
                { "trip_am_peak", Math.Round(road.TotalAmPeakTrips, 0) },
                { "trip_pm_peak", Math.Round(road.TotalPmPeakTrips, 0) },
                { "parcel_count", road.ParcelCount },
                { "vc_ratio", road.VolumeToCapacityRatio },
            };

            fc.Add(new Feature(road.Geometry, attrs));
        }

        var json = writer.Write(fc);
        File.WriteAllText(outputPath, json);
        _logger.LogInformation("Wrote {Count} road features to {Path}", fc.Count, outputPath);
    }

    /// <summary>
    /// Write summary report as JSON.
    /// </summary>
    public void WriteSummary(string outputPath, TripGenerationSummary summary)
    {
        _logger.LogInformation("Writing summary to {Path}", outputPath);
        EnsureDirectory(outputPath);

        var json = JsonSerializer.Serialize(summary, JsonOpts);
        File.WriteAllText(outputPath, json);
        _logger.LogInformation("Summary written successfully");
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
