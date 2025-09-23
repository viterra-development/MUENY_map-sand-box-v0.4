namespace NoaaDataProcessor.Models;

public class RainfallPoint
{
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public double RainfallValue { get; set; }
    public double RainfallInches => Math.Round(RainfallValue / 100.0, 2); // Convert from 0.01 inches to inches
}