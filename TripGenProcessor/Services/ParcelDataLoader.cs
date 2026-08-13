using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Loads parcel and road GeoJSON files into typed models.
/// Handles attribute mapping from various CAD export formats.
/// </summary>
public class ParcelDataLoader
{
    private readonly ILogger<ParcelDataLoader> _logger;
    private readonly GeoJsonReader _reader;

    public ParcelDataLoader(ILogger<ParcelDataLoader> logger)
    {
        _logger = logger;
        _reader = new GeoJsonReader();
    }

    /// <summary>
    /// Load parcels from a CAD-export GeoJSON file.
    /// Attempts multiple attribute name variants for robustness.
    /// </summary>
    public List<CadParcel> LoadParcels(string path)
    {
        _logger.LogInformation("Loading parcels from {Path}", path);

        var json = File.ReadAllText(path);
        var fc = _reader.Read<FeatureCollection>(json);
        var parcels = new List<CadParcel>();
        int skipped = 0;

        foreach (var feature in fc)
        {
            try
            {
                var parcel = MapFeatureToParcel(feature);
                if (parcel != null)
                    parcels.Add(parcel);
                else
                    skipped++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse parcel feature: {Error}", ex.Message);
                skipped++;
            }
        }

        _logger.LogInformation("Loaded {Count} parcels ({Skipped} skipped) from {Path}",
            parcels.Count, skipped, Path.GetFileName(path));
        return parcels;
    }

    /// <summary>
    /// Load TIGER road segments from GeoJSON.
    /// </summary>
    public List<RoadSegment> LoadRoads(string path)
    {
        _logger.LogInformation("Loading roads from {Path}", path);

        var json = File.ReadAllText(path);
        var fc = _reader.Read<FeatureCollection>(json);
        var roads = new List<RoadSegment>();

        foreach (var feature in fc)
        {
            try
            {
                var road = MapFeatureToRoad(feature);
                if (road != null)
                    roads.Add(road);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse road feature: {Error}", ex.Message);
            }
        }

        _logger.LogInformation("Loaded {Count} road segments from {Path}",
            roads.Count, Path.GetFileName(path));
        return roads;
    }

    /// <summary>
    /// Load a city boundary polygon for clipping parcels.
    /// Expects a FeatureCollection; finds the feature matching the city name.
    /// </summary>
    public Geometry? LoadCityBoundary(string path, string cityName)
    {
        _logger.LogInformation("Loading city boundary for '{City}' from {Path}", cityName, path);

        var json = File.ReadAllText(path);
        var fc = _reader.Read<FeatureCollection>(json);

        foreach (var feature in fc)
        {
            var name = GetAttr(feature, "NAME", "name", "CITY_NM", "city_nm", "CityName");
            if (name != null && name.Contains(cityName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Found boundary for '{City}' with {Points} vertices",
                    cityName, feature.Geometry.NumPoints);
                return feature.Geometry;
            }
        }

        _logger.LogWarning("City boundary for '{City}' not found in {Path}", cityName, path);
        return null;
    }

    private CadParcel? MapFeatureToParcel(IFeature feature)
    {
        if (feature.Geometry == null) return null;

        var parcelId = GetAttr(feature,
            "prop_id", "PROP_ID", "parcel_id", "PARCEL_ID",
            "geo_id", "GEO_ID", "OBJECTID", "FID") ?? "";

        var stateCd = GetAttr(feature,
            "state_cd", "STATE_CD", "sptb_cd", "SPTB_CD",
            "land_use", "LAND_USE", "lu_code", "LU_CODE",
            "property_type", "PROPERTY_TYPE") ?? "";

        if (string.IsNullOrWhiteSpace(parcelId) && string.IsNullOrWhiteSpace(stateCd))
            return null;

        return new CadParcel
        {
            ParcelId = parcelId,
            StateCd = stateCd.Trim().ToUpperInvariant(),
            SitusStreet = GetAttr(feature, "situs_street", "SITUS_STREET", "situs_addr", "address", "ADDRESS"),
            SitusCity = GetAttr(feature, "situs_city", "SITUS_CITY", "city", "CITY"),
            OwnerName = GetAttr(feature, "owner_name", "OWNER_NAME", "owner", "OWNER"),
            LegalDesc = GetAttr(feature, "legal_desc", "LEGAL_DESC", "legal_description", "LEGAL_DESCRIPTION"),
            LegalAcreage = GetDouble(feature, "legal_acreage", "LEGAL_ACREAGE", "acreage", "ACREAGE", "acres", "ACRES"),
            ImprvSqft = GetDouble(feature, "imprv_sqft", "IMPRV_SQFT", "bldg_sqft", "BLDG_SQFT", "sqft", "SQFT",
                "imprv_area", "IMPRV_AREA"),
            ImprvVal = GetDouble(feature, "imprv_val", "IMPRV_VAL", "improvement_value", "IMPROVEMENT_VALUE"),
            LandVal = GetDouble(feature, "land_val", "LAND_VAL", "land_value", "LAND_VALUE"),
            TotalVal = GetDouble(feature, "total_val", "TOTAL_VAL", "appraised_val", "APPRAISED_VAL",
                "market_val", "MARKET_VAL"),
            YearBuilt = GetInt(feature, "year_built", "YEAR_BUILT", "yr_built", "YR_BUILT"),
            DwellingUnits = GetInt(feature, "dwelling_units", "DWELLING_UNITS", "du", "DU",
                "num_units", "NUM_UNITS", "units"),
            Geometry = feature.Geometry
        };
    }

    private RoadSegment? MapFeatureToRoad(IFeature feature)
    {
        if (feature.Geometry == null) return null;

        var linearId = GetAttr(feature,
            "LINEARID", "linearid", "linear_id", "LINEAR_ID",
            "OBJECTID", "FID", "id") ?? "";

        return new RoadSegment
        {
            LinearId = linearId,
            FullName = GetAttr(feature,
                "FULLNAME", "fullname", "full_name", "FULL_NAME",
                "name", "NAME", "road_name", "ROAD_NAME") ?? "Unknown",
            Mtfcc = GetAttr(feature, "MTFCC", "mtfcc"),
            Aadt = GetInt(feature, "AADT", "aadt", "traffic_count", "TRAFFIC_COUNT",
                "avg_daily_traffic", "AVG_DAILY_TRAFFIC"),
            AadtYear = GetInt(feature, "aadt_year", "AADT_YEAR", "count_year", "COUNT_YEAR"),
            FunctionalClass = GetAttr(feature, "func_class", "FUNC_CLASS", "functional_class",
                "FUNCTIONAL_CLASS"),
            Geometry = feature.Geometry
        };
    }

    /// <summary>
    /// Get a string attribute, trying multiple name variants.
    /// </summary>
    private static string? GetAttr(IFeature feature, params string[] names)
    {
        foreach (var name in names)
        {
            if (feature.Attributes?.Exists(name) == true)
            {
                var val = feature.Attributes[name];
                if (val != null)
                    return val.ToString()?.Trim();
            }
        }
        return null;
    }

    private static double GetDouble(IFeature feature, params string[] names)
    {
        var val = GetAttr(feature, names);
        if (val != null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0.0;
    }

    private static int? GetInt(IFeature feature, params string[] names)
    {
        var val = GetAttr(feature, names);
        if (val != null && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        return null;
    }
}
