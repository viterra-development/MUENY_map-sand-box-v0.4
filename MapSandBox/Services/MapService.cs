using MapSandBox.Models;

namespace MapSandBox.Services;

public class MapService
{
    public MapConfig GetDefaultConfig()
    {
        return new MapConfig
        {
            Latitude = 51.47,
            Longitude = 0.45,
            Zoom = 3,
            Bearing = 0,
            Pitch = 90,
            Layers = GetDefaultLayers()
        };
    }
    
    public List<LayerConfig> GetDefaultLayers()
    {
        return new List<LayerConfig>
        {
            new LayerConfig
            {
                Id = "countries",
                Type = "GeoJson",
                DataUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_50m_admin_0_scale_rank.geojson",
                Visible = true,
                Properties = new Dictionary<string, object>()
            },
            new LayerConfig
            {
                Id = "cris-crashes",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-crashes-traffic-roads.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["radiusMinPixels"] = 4,
                    ["radiusMaxPixels"] = 15,
                    ["radiusScale"] = 100,
                    ["getPosition"] = "@@=d.geometry.coordinates",
                    ["getRadius"] = "@@=d.properties.TotalPersons || 1",
                    ["getFillColor"] = "@@=getCrashSeverityColor(d.properties.CrashSeverity)"
                }
            },
            new LayerConfig
            {
                Id = "cris-risk-segments",
                Type = "PathLayer",
                DataUrl = "/cris-data/parker-county-risk-segments-traffic.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["getPath"] = "@@=d.geometry.coordinates",
                    ["getWidth"] = "@@=Math.max(2, (d.properties.Aadt || 100) / 1000)",
                    ["getColor"] = "@@=getRiskLevelColor(d.properties.RiskLevel)",
                    ["widthMinPixels"] = 2,
                    ["widthMaxPixels"] = 20
                }
            },
            new LayerConfig
            {
                Id = "cris-intersections",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-intersection-risks.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["radiusMinPixels"] = 6,
                    ["radiusMaxPixels"] = 25,
                    ["getPosition"] = "@@=d.geometry.coordinates",
                    ["getRadius"] = "@@=Math.sqrt(d.properties.CrashCount) * 100",
                    ["getFillColor"] = "@@=getRiskLevelColor(d.properties.RiskLevel)",
                    ["stroked"] = true,
                    ["getLineColor"] = "[0, 0, 0, 255]",
                    ["lineWidthMinPixels"] = 1
                }
            }
        };
    }
    
    public List<LayerInfo> GetLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "countries", Name = "Countries", Visible = true },
            new LayerInfo { Id = "cris-crashes", Name = "CRIS Crash Points", Visible = false },
            new LayerInfo { Id = "cris-risk-segments", Name = "CRIS Risk Segments", Visible = false },
            new LayerInfo { Id = "cris-intersections", Name = "CRIS Intersection Risks", Visible = false }
        };
    }
} 