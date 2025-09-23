namespace NoaaDataProcessor.Models;

public class GridHeader
{
    public int NCols { get; set; }
    public int NRows { get; set; }
    public double XllCorner { get; set; }
    public double YllCorner { get; set; }
    public double CellSize { get; set; }
    public double NoDataValue { get; set; }

    public double XurCorner => XllCorner + (NCols * CellSize);
    public double YurCorner => YllCorner + (NRows * CellSize);
}