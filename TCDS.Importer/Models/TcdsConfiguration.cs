namespace TCDS.Importer.Models;

public class TcdsConfiguration
{
    public bool Headless { get; set; } = false;
    public string Browser { get; set; } = "chromium";
    public int Timeout { get; set; } = 120000;
    public bool UseSystemChrome { get; set; } = true;
    public int WaitDelay { get; set; } = 5000;
    public int MinWaitDelay { get; set; } = 3000;
    public int MaxWaitDelay { get; set; } = 8000;
    public int NavigationTimeout { get; set; } = 180000;
    public int AjaxWaitTimeout { get; set; } = 60000;
    public bool UseFuzzyWaits { get; set; } = true;
    public string TargetUrl { get; set; } = "https://txdot.public.ms2soft.com/tcds/tsearch.asp?mod=tcds";
    public string ScreenshotDirectory { get; set; } = "Screenshots";
    public string DataDirectory { get; set; } = "Data";
}