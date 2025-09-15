using System.Text.Json.Serialization;

namespace MapSandBox.Models;

// SSURGO API response models
public class SsurgoApiResponse
{
    [JsonPropertyName("Table")]
    public List<SsurgoRecord> Table { get; set; } = new();
}

public class SsurgoRecord
{
    [JsonPropertyName("mukey")]
    public string MuKey { get; set; } = "";
    
    [JsonPropertyName("musym")]
    public string MuSym { get; set; } = "";
    
    [JsonPropertyName("muname")]
    public string MuName { get; set; } = "";
    
    [JsonPropertyName("soil_clay_pct")]
    public decimal? SoilClayPct { get; set; }
    
    [JsonPropertyName("soil_ksat_um_per_s")]
    public decimal? SoilKsatUmPerS { get; set; }
    
    [JsonPropertyName("component_pct")]
    public int? ComponentPct { get; set; }
    
    [JsonPropertyName("geom")]
    public string Geom { get; set; } = ""; // WKT format from SSURGO
}

// GeoJSON output models
public class SoilGeoJsonFeature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();
    
    [JsonPropertyName("geometry")]
    public object Geometry { get; set; } = null!;
}

public class SoilGeoJsonCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";
    
    [JsonPropertyName("features")]
    public List<SoilGeoJsonFeature> Features { get; set; } = new();
}

// Geometry conversion models
public class GeoJsonGeometry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("coordinates")]
    public object Coordinates { get; set; } = null!;
}