namespace MapSandBox.Models;

public class SoilDataConfig
{
    public string BaseUrl { get; set; } = "";
    public string CdnUrl { get; set; } = "";
    public bool UseCdn { get; set; } = true;
    public bool UseLocal { get; set; } = false;

    public string GetSoilDataUrl(string fileName)
    {
        if (UseLocal)
        {
            return $"/soil-data/{fileName}";
        }

        return UseCdn ? $"{CdnUrl}/soil-data/{fileName}" : $"{BaseUrl}/soil-data/{fileName}";
    }
}