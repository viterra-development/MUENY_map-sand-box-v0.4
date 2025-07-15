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
                Id = "rivers",
                Type = "GeoJson",
                DataUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_50m_rivers_lake_centerlines.geojson",
                Visible = true,
                Properties = new Dictionary<string, object>()
            },
            new LayerConfig
            {
                Id = "airports",
                Type = "GeoJson",
                DataUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_10m_airports.geojson",
                Visible = true,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["pointRadiusMinPixels"] = 2,
                    ["pointRadiusScale"] = 2000,
                    ["getFillColor"] = new int[] { 200, 0, 80, 180 },
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "flight-paths",
                Type = "GreatCircle",
                DataUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_10m_airports.geojson",
                Visible = true,
                Properties = new Dictionary<string, object>
                {
                    ["getSourceColor"] = new int[] { 0, 128, 200 },
                    ["getTargetColor"] = new int[] { 200, 0, 80 },
                    ["getWidth"] = 1
                }
            }
        };
    }
    
    public List<LayerInfo> GetLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "countries", Name = "Countries", Visible = true },
            new LayerInfo { Id = "rivers", Name = "Rivers", Visible = true },
            new LayerInfo { Id = "airports", Name = "Airports", Visible = true },
            new LayerInfo { Id = "flight-paths", Name = "Flight Paths", Visible = true }
        };
    }
} 