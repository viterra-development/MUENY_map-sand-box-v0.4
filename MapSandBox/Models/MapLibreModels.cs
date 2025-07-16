namespace MapSandBox.Models;

public class MapLibreConfig
{
    public double Latitude { get; set; } = 51.47;
    public double Longitude { get; set; } = 0.45;
    public double Zoom { get; set; } = 3;
    public double Bearing { get; set; } = 0;
    public double Pitch { get; set; } = 0;
    public BaseMapConfig BaseMap { get; set; } = new();
    public List<LayerConfig> Layers { get; set; } = new();
}

public class BaseMapConfig
{
    public string Style { get; set; } = "https://basemaps.cartocdn.com/gl/positron-gl-style/style.json";
    public bool ShowControls { get; set; } = true;
    public bool ShowAttribution { get; set; } = true;
    public string Name { get; set; } = "Light";
}

public class BaseMapStyle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
}

public class MapLibreClickEventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public object? Object { get; set; }
    public string LayerId { get; set; } = "";
}