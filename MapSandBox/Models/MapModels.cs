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

public class CrashPopupData
{
    public string CrashId { get; set; } = "";
    public string CrashDate { get; set; } = "";
    public string CrashTime { get; set; } = "";
    public string CrashDateTime { get; set; } = "";
    public string Severity { get; set; } = "";
    public string SeverityCode { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int PersonsInvolved { get; set; }
    public int VehiclesInvolved { get; set; }
    public int FatalCount { get; set; }
    public int InjuryCount { get; set; }
    public string WeatherCondition { get; set; } = "";
    public string LightCondition { get; set; } = "";
    public string SurfaceCondition { get; set; } = "";
    public string RoadwayId { get; set; } = "";
    public List<string> ContributingFactors { get; set; } = new();

    // DEM-derived slope information
    public decimal SlopeAtLocation { get; set; }  // Degrees from DEM
    public decimal SlopePercentage { get; set; }  // Percentage grade
    public string SlopeCategory { get; set; } = ""; // Flat/Moderate/Steep

    public string FormattedCoordinates => $"{Longitude:F6}, {Latitude:F6}";
}

public class CrashClusterData
{
    public List<CrashPopupData> Crashes { get; set; } = new();
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int TotalCrashes => Crashes.Count;
    public string FormattedCoordinates => $"{Longitude:F6}, {Latitude:F6}";
} 