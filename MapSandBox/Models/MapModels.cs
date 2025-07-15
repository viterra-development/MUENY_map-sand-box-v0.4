namespace MapSandBox.Models;

public class MapConfig
{
    public double Latitude { get; set; } = 51.47;
    public double Longitude { get; set; } = 0.45;
    public double Zoom { get; set; } = 3;
    public double Bearing { get; set; } = 0;
    public double Pitch { get; set; } = 90;
    public List<LayerConfig> Layers { get; set; } = new();
}

public class LayerConfig
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string DataUrl { get; set; } = "";
    public bool Visible { get; set; } = true;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class LayerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
}

public class LayerToggleEventArgs
{
    public string LayerId { get; set; }
    public bool IsVisible { get; set; }
    
    public LayerToggleEventArgs(string layerId, object? value)
    {
        LayerId = layerId;
        IsVisible = value is bool b ? b : false;
    }
}

public class MapClickEventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public object? Object { get; set; }
} 