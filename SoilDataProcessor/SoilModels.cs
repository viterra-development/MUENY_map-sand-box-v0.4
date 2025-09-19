using System.Text.Json.Serialization;

namespace SoilDataProcessor;

// Strongly-typed soil properties for GeoJSON features
public class SoilProperties
{
    [JsonPropertyName("mukey")]
    public string MuKey { get; set; } = "";

    [JsonPropertyName("musym")]
    public string MuSym { get; set; } = "";

    [JsonPropertyName("muname")]
    public string MuName { get; set; } = "";

    [JsonPropertyName("soil_clay_pct")]
    public double SoilClayPct { get; set; }

    [JsonPropertyName("soil_ksat_um_per_s")]
    public double SoilKsatUmPerS { get; set; }

    [JsonPropertyName("polygon_count")]
    public int? PolygonCount { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}

// Specialized properties for visualization layers
public class SoilVisualizationProperties
{
    [JsonPropertyName("mukey")]
    public string MuKey { get; set; } = "";

    [JsonPropertyName("musym")]
    public string MuSym { get; set; } = "";

    [JsonPropertyName("muname")]
    public string MuName { get; set; } = "";

    [JsonPropertyName("visualization")]
    public string Visualization { get; set; } = "";
}

public class ClayVisualizationProperties : SoilVisualizationProperties
{
    [JsonPropertyName("soil_clay_pct")]
    public double SoilClayPct { get; set; }
}

public class KsatVisualizationProperties : SoilVisualizationProperties
{
    [JsonPropertyName("soil_ksat_um_per_s")]
    public double SoilKsatUmPerS { get; set; }
}

public class SsurgoApiResponse
{
    [JsonPropertyName("Table")]
    public List<string[]> Table { get; set; } = new();
}

public class SsurgoTableRow
{
    [JsonPropertyName("mukey")]
    public string MuKey { get; set; } = string.Empty;
    
    [JsonPropertyName("musym")]
    public string MuSym { get; set; } = string.Empty;
    
    [JsonPropertyName("muname")]
    public string? MuName { get; set; }
    
    [JsonPropertyName("cokey")]
    public string CoKey { get; set; } = string.Empty;
    
    [JsonPropertyName("compname")]
    public string? CompName { get; set; }
    
    [JsonPropertyName("component_pct")]
    public double? ComponentPct { get; set; }
    
    [JsonPropertyName("soil_clay_pct")]
    public double? SoilClayPct { get; set; }
    
    [JsonPropertyName("soil_ksat_um_per_s")]
    public double? SoilKsatUmPerS { get; set; }
    
    [JsonPropertyName("geom")]
    public string Geom { get; set; } = string.Empty;
}

public class SoilGeoJsonCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";
    
    [JsonPropertyName("features")]
    public List<SoilGeoJsonFeature> Features { get; set; } = new();
}

public class SoilGeoJsonFeature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();
    
    [JsonPropertyName("geometry")]
    public object? Geometry { get; set; }
}