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
            }
        };
    }
    
    public List<LayerInfo> GetLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "countries", Name = "Countries", Visible = true }
        };
    }
} 