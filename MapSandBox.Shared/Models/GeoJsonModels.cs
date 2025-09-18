using System.Text.Json.Serialization;

namespace MapSandBox.Shared.Models;

// Base GeoJSON geometry models
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PointGeometry), "Point")]
[JsonDerivedType(typeof(LineStringGeometry), "LineString")]
[JsonDerivedType(typeof(MultiLineStringGeometry), "MultiLineString")]
public abstract class GeoJsonGeometry
{
}

public class PointGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double> Coordinates { get; set; } = new(); // [lon, lat]
}

public class LineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<double>> Coordinates { get; set; } = new(); // [[lon, lat], [lon, lat], ...]
}

public class MultiLineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<List<double>>> Coordinates { get; set; } = new(); // [[[lon, lat], ...], [[lon, lat], ...]
}

// Base GeoJSON feature models
public class GeoJsonFeature<TProperties> where TProperties : class
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";

    [JsonPropertyName("geometry")]
    public GeoJsonGeometry? Geometry { get; set; }

    [JsonPropertyName("properties")]
    public TProperties Properties { get; set; } = null!;
}

public class GeoJsonFeatureCollection<TProperties> where TProperties : class
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("features")]
    public List<GeoJsonFeature<TProperties>> Features { get; set; } = new();
}

// Legacy support for untyped properties (for backwards compatibility)
public class GeoJsonFeatureUntyped
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";

    [JsonPropertyName("geometry")]
    public GeoJsonGeometry? Geometry { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?>? Properties { get; set; }
}

public class GeoJsonFeatureCollectionUntyped
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("features")]
    public List<GeoJsonFeatureUntyped> Features { get; set; } = new();
}