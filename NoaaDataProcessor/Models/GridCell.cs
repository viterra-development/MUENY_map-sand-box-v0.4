namespace NoaaDataProcessor.Models;

public class GridCell
{
    public int Row { get; set; }
    public int Col { get; set; }
    public double Value { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }

    public bool IsNoData(double noDataValue) => Math.Abs(Value - noDataValue) < 0.001;
}