namespace TCDS.Importer.Models;

public class TcdsConfiguration
{
    public bool Headless { get; set; } = false;
    public string Browser { get; set; } = "chromium";
    public int Timeout { get; set; } = 60000;
    public bool UseSystemChrome { get; set; } = true;
    public int WaitDelay { get; set; } = 5000;
    public string TargetUrl { get; set; } = "https://txdot.public.ms2soft.com/tcds/tsearch.asp?mod=tcds";
    public string ScreenshotDirectory { get; set; } = "Screenshots";
}