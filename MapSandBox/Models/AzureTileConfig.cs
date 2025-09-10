namespace MapSandBox.Models;

public class AzureTileConfig
{
    public string BaseUrl { get; set; } = "";
    public string CdnUrl { get; set; } = "";
    public bool UseCdn { get; set; } = true;
    public string GetTileUrl(string tileType) => UseCdn ? $"{CdnUrl}/tiles/{tileType}" : $"{BaseUrl}/tiles/{tileType}";
}