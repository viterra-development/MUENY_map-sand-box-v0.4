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

public class RoadPopupData
{
    public required string RoadName { get; set; }
    public required string RoadType { get; set; }
    public required string RoadTypeName { get; set; }
    public int? AADT { get; set; }
    public string? AADTYear { get; set; }
    public int? DHV30 { get; set; }
    public string? LocationId { get; set; }
    public string? LocatedOn { get; set; }
    public string? LinearId { get; set; }
    public string? MTFCC { get; set; }
    public required double[] Coordinates { get; set; }

    public bool HasTrafficData => AADT.HasValue && AADT.Value > 0;
    
    public string FormattedAADT => AADT?.ToString("N0") ?? "No data";
    
    public string FormattedCoordinates => 
        $"{Coordinates[1]:F6}, {Coordinates[0]:F6}";
}

public class SoilPopupData
{
    public string? MuSym { get; set; }
    public string? MuName { get; set; }
    public string? MuKey { get; set; }
    public double? SoilClayPct { get; set; }
    public double? SoilKsatUmPerS { get; set; }
    public required double[] Coordinates { get; set; }

    public string FormattedClayPct => SoilClayPct.HasValue ? SoilClayPct.Value.ToString("F1") + "%" : "N/A";
    public string FormattedKsat => SoilKsatUmPerS.HasValue ? SoilKsatUmPerS.Value.ToString("F3") + " μm/s" : "N/A";
    public string FormattedCoordinates => $"{Coordinates[1]:F6}, {Coordinates[0]:F6}";
}