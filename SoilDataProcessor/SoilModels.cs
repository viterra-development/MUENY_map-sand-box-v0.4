using System.Text.Json.Serialization;

namespace SoilDataProcessor;

public class SsurgoApiResponse
{
    [JsonPropertyName("Table")]
    public List<SsurgoTableRow> Table { get; set; } = new();
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